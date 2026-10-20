using System.IO.Compression;
using System.Text;
using SecurityManager.Network.Application.Abstractions;
using SecurityManager.Network.Application.DTOs;
using SecurityManager.Network.Application.Mapping;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Network.Domain.Services;
using SecurityManager.Shared.Kernel.Exceptions;

namespace SecurityManager.Network.Application.Services;

public interface IGatewayService
{
    Task<IReadOnlyList<GatewayDto>> GetAllAsync(CancellationToken ct = default);
    Task<GatewayDetailDto> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<GatewayDto> CreateAsync(UpsertGatewayRequest request, CancellationToken ct = default);
    Task<GatewayDto> UpdateAsync(Guid id, UpsertGatewayRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<GeneratedConfigDto> GenerateConfigAsync(Guid id, CancellationToken ct = default);
    Task<(string FileName, byte[] Content)> GenerateBundleAsync(Guid id, CancellationToken ct = default);
    Task<GatewayDto> MarkDeployedAsync(Guid id, string? actor, CancellationToken ct = default);
}

public sealed class GatewayService(INetworkUnitOfWork uow, GatewayLoader loader) : IGatewayService
{
    public async Task<IReadOnlyList<GatewayDto>> GetAllAsync(CancellationToken ct = default)
    {
        var gateways = await loader.LoadManyAsync(o => o.OrderBy(g => g.Name), null, ct);
        var clusters = (await uow.Clusters.ListAsync(includes: [c => c.VirtualIps], ct: ct)).ToDictionary(c => c.Id);

        return gateways.Select(g =>
        {
            var cluster = g.ClusterId is { } cid ? clusters.GetValueOrDefault(cid) : null;
            var members = cluster is null ? [g] : gateways.Where(m => m.ClusterId == cluster.Id).ToList();
            return g.ToDto(cluster, members, ConfigValidator.Validate(g, cluster, members));
        }).ToList();
    }

    public async Task<GatewayDetailDto> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var g = await loader.LoadAsync(id, ct);
        var (cluster, members) = await loader.LoadHaContextAsync(g, ct);
        var issues = ConfigValidator.Validate(g, cluster, members);
        return new GatewayDetailDto(
            g.ToDto(cluster, members, issues),
            g.Interfaces.OrderBy(i => i.Name).Select(i => i.ToDto()).ToList(),
            g.Routes.OrderBy(r => r.Table).ThenBy(r => r.Metric).Select(r => r.ToDto()).ToList(),
            g.Policies.OrderBy(p => p.Priority).Select(p => p.ToDto()).ToList(),
            g.NatRules.OrderBy(n => n.Priority).ThenBy(n => n.Name).Select(n => n.ToDto()).ToList(),
            g.Tunnels.OrderBy(t => t.Name).Select(t => t.ToDto()).ToList(),
            issues);
    }

    public async Task<GatewayDto> CreateAsync(UpsertGatewayRequest r, CancellationToken ct = default)
    {
        var g = new Gateway();
        await ApplyAsync(g, r, ct);
        await uow.Gateways.AddAsync(g, ct);
        await uow.SaveChangesAsync(ct);
        return await SummaryAsync(g.Id, ct);
    }

    public async Task<GatewayDto> UpdateAsync(Guid id, UpsertGatewayRequest r, CancellationToken ct = default)
    {
        var g = await loader.LoadAsync(id, ct);
        var previousCluster = g.ClusterId;
        await ApplyAsync(g, r, ct);
        g.Touch();

        // Membership or priority changes alter every member's keepalived config.
        foreach (var clusterId in new[] { previousCluster, g.ClusterId }.Where(c => c is not null).Distinct())
            await TouchClusterMembersAsync(clusterId!.Value, except: g.Id, ct);

        await uow.SaveChangesAsync(ct);
        return await SummaryAsync(g.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var g = await loader.LoadAsync(id, ct);
        if (g.ClusterId is { } clusterId) await TouchClusterMembersAsync(clusterId, except: g.Id, ct);
        uow.Gateways.Remove(g); // configuration items cascade
        await uow.SaveChangesAsync(ct);
    }

    public async Task<GeneratedConfigDto> GenerateConfigAsync(Guid id, CancellationToken ct = default)
    {
        var g = await loader.LoadAsync(id, ct);
        var (cluster, members) = await loader.LoadHaContextAsync(g, ct);
        return new GeneratedConfigDto(g.Id, g.Hostname, g.ConfigRevision,
            LinuxConfigGenerator.Generate(g, cluster, members), ConfigValidator.Validate(g, cluster, members));
    }

    public async Task<(string FileName, byte[] Content)> GenerateBundleAsync(Guid id, CancellationToken ct = default)
    {
        var config = await GenerateConfigAsync(id, ct);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in config.Files)
            {
                var entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                // Unix line endings: these files are executed on Linux.
                var bytes = new UTF8Encoding(false).GetBytes(file.Content.Replace("\r\n", "\n"));
                await stream.WriteAsync(bytes, ct);
            }
        }
        return ($"{config.Hostname}-r{config.Revision}.zip", buffer.ToArray());
    }

    public async Task<GatewayDto> MarkDeployedAsync(Guid id, string? actor, CancellationToken ct = default)
    {
        var g = await uow.Gateways.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Gateway", id);
        g.MarkDeployed(actor, DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return await SummaryAsync(id, ct);
    }

    private async Task ApplyAsync(Gateway g, UpsertGatewayRequest r, CancellationToken ct)
    {
        g.Name = r.Name.Trim();
        g.Hostname = r.Hostname.Trim().ToLowerInvariant();
        g.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        g.ManagementAddress = string.IsNullOrWhiteSpace(r.ManagementAddress) ? null : r.ManagementAddress.Trim();
        g.HaPriority = r.HaPriority;
        g.ClusterId = r.ClusterId;

        var errors = g.Validate().ToList();
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));
        if (r.ClusterId is { } cid && !await uow.Clusters.AnyAsync(c => c.Id == cid, ct))
            throw new BusinessRuleException("The selected HA cluster does not exist.");

        var name = g.Name;
        var host = g.Hostname;
        var id = g.Id;
        if (await uow.Gateways.AnyAsync(x => (x.Name == name || x.Hostname == host) && x.Id != id, ct))
            throw new ConflictException($"A gateway named '{name}' or with hostname '{host}' already exists.");
    }

    private async Task TouchClusterMembersAsync(Guid clusterId, Guid except, CancellationToken ct)
    {
        foreach (var member in await uow.Gateways.ListAsync(m => m.ClusterId == clusterId && m.Id != except, ct: ct))
            member.Touch();
    }

    private async Task<GatewayDto> SummaryAsync(Guid id, CancellationToken ct) => (await GetDetailAsync(id, ct)).Gateway;
}
