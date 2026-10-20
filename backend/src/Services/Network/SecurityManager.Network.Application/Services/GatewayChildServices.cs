using SecurityManager.Network.Application.Abstractions;
using SecurityManager.Network.Application.DTOs;
using SecurityManager.Network.Application.Mapping;
using SecurityManager.Network.Domain.Entities;
using SecurityManager.Shared.Kernel.Abstractions;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Networking;

namespace SecurityManager.Network.Application.Services;

public interface IGatewayChildService<TDto, in TRequest>
{
    Task<IReadOnlyList<TDto>> ListAsync(Guid gatewayId, CancellationToken ct = default);
    Task<TDto> CreateAsync(Guid gatewayId, TRequest request, CancellationToken ct = default);
    Task<TDto> UpdateAsync(Guid gatewayId, Guid id, TRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid gatewayId, Guid id, CancellationToken ct = default);
}

/// <summary>
/// CRUD for configuration items owned by a gateway. Every change validates the item, enforces uniqueness,
/// and bumps the gateway's configuration revision so the UI can show "pending deployment".
/// </summary>
public abstract class GatewayChildService<TEntity, TDto, TRequest>(INetworkUnitOfWork uow) : IGatewayChildService<TDto, TRequest>
    where TEntity : GatewayChild, new()
{
    protected INetworkUnitOfWork Uow => uow;
    protected abstract IRepository<TEntity> Repository { get; }
    protected abstract string ItemName { get; }
    protected abstract void Apply(TEntity entity, TRequest request);
    protected abstract TDto Map(TEntity entity);
    protected virtual IOrderedQueryable<TEntity> Order(IQueryable<TEntity> q) => q.OrderBy(e => e.CreatedAt);

    /// <summary>Throw <see cref="ConflictException"/> if the item clashes with a sibling.</summary>
    protected virtual Task EnsureUniqueAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

    public async Task<IReadOnlyList<TDto>> ListAsync(Guid gatewayId, CancellationToken ct = default)
    {
        await LoadGatewayAsync(gatewayId, ct);
        var items = await Repository.ListAsync(e => e.GatewayId == gatewayId, Order, ct: ct);
        return items.Select(Map).ToList();
    }

    public async Task<TDto> CreateAsync(Guid gatewayId, TRequest request, CancellationToken ct = default)
    {
        var gateway = await LoadGatewayAsync(gatewayId, ct);
        var entity = new TEntity { GatewayId = gatewayId };
        await ApplyAndValidateAsync(entity, request, ct);
        await Repository.AddAsync(entity, ct);
        gateway.Touch();
        await uow.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<TDto> UpdateAsync(Guid gatewayId, Guid id, TRequest request, CancellationToken ct = default)
    {
        var gateway = await LoadGatewayAsync(gatewayId, ct);
        var entity = await LoadItemAsync(gatewayId, id, ct);
        await ApplyAndValidateAsync(entity, request, ct);
        gateway.Touch();
        await uow.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid gatewayId, Guid id, CancellationToken ct = default)
    {
        var gateway = await LoadGatewayAsync(gatewayId, ct);
        Repository.Remove(await LoadItemAsync(gatewayId, id, ct));
        gateway.Touch();
        await uow.SaveChangesAsync(ct);
    }

    protected async Task<TEntity> LoadItemAsync(Guid gatewayId, Guid id, CancellationToken ct) =>
        await Repository.FirstOrDefaultAsync(e => e.Id == id && e.GatewayId == gatewayId, ct: ct)
        ?? throw new NotFoundException(ItemName, id);

    private async Task ApplyAndValidateAsync(TEntity entity, TRequest request, CancellationToken ct)
    {
        Apply(entity, request);
        var errors = entity.Validate().ToList();
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));
        await EnsureUniqueAsync(entity, ct);
    }

    private async Task<Gateway> LoadGatewayAsync(Guid id, CancellationToken ct) =>
        await uow.Gateways.GetByIdAsync(id, ct: ct) ?? throw new NotFoundException("Gateway", id);

    protected static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ---------------------------------------------------------------------------------------------- Interfaces
public interface IInterfaceService : IGatewayChildService<InterfaceDto, UpsertInterfaceRequest>;

public sealed class InterfaceService(INetworkUnitOfWork uow)
    : GatewayChildService<NetworkInterface, InterfaceDto, UpsertInterfaceRequest>(uow), IInterfaceService
{
    protected override IRepository<NetworkInterface> Repository => Uow.Interfaces;
    protected override string ItemName => "Interface";
    protected override IOrderedQueryable<NetworkInterface> Order(IQueryable<NetworkInterface> q) => q.OrderBy(i => i.Name);
    protected override InterfaceDto Map(NetworkInterface e) => e.ToDto();

    protected override void Apply(NetworkInterface e, UpsertInterfaceRequest r)
    {
        e.Name = r.Name.Trim();
        e.Type = r.Type;
        e.Zone = r.Zone;
        e.Mode = r.Mode;
        e.Address = Clean(r.Address);
        e.Mtu = r.Mtu;
        e.VlanId = r.VlanId;
        e.Parent = Clean(r.Parent);
        e.Members = Clean(r.Members) is { } m ? string.Join(",", m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) : null;
        e.IsEnabled = r.IsEnabled;
        e.Description = Clean(r.Description);
    }

    protected override async Task EnsureUniqueAsync(NetworkInterface e, CancellationToken ct)
    {
        if (await Uow.Interfaces.AnyAsync(x => x.GatewayId == e.GatewayId && x.Name == e.Name && x.Id != e.Id, ct))
            throw new ConflictException($"Interface '{e.Name}' already exists on this gateway.");
    }
}

// ---------------------------------------------------------------------------------------------- Routes
public interface IRouteService : IGatewayChildService<RouteDto, UpsertRouteRequest>;

public sealed class RouteService(INetworkUnitOfWork uow)
    : GatewayChildService<StaticRoute, RouteDto, UpsertRouteRequest>(uow), IRouteService
{
    protected override IRepository<StaticRoute> Repository => Uow.Routes;
    protected override string ItemName => "Route";
    protected override IOrderedQueryable<StaticRoute> Order(IQueryable<StaticRoute> q) => q.OrderBy(r => r.Table).ThenBy(r => r.Metric);
    protected override RouteDto Map(StaticRoute e) => e.ToDto();

    protected override void Apply(StaticRoute e, UpsertRouteRequest r)
    {
        // Normalise "10.0.0.5/24" to its network "10.0.0.0/24".
        e.Destination = Cidr.TryParse(r.Destination, out var c) ? c.NetworkText : r.Destination.Trim();
        e.NextHop = Clean(r.NextHop);
        e.Interface = Clean(r.Interface);
        e.Metric = r.Metric;
        e.Table = r.Table;
        e.IsEnabled = r.IsEnabled;
        e.Description = Clean(r.Description);
    }
}

// ---------------------------------------------------------------------------------------------- Policy routing
public interface IRoutingPolicyService : IGatewayChildService<RoutingPolicyDto, UpsertRoutingPolicyRequest>;

public sealed class RoutingPolicyService(INetworkUnitOfWork uow)
    : GatewayChildService<RoutingPolicy, RoutingPolicyDto, UpsertRoutingPolicyRequest>(uow), IRoutingPolicyService
{
    protected override IRepository<RoutingPolicy> Repository => Uow.Policies;
    protected override string ItemName => "Routing policy";
    protected override IOrderedQueryable<RoutingPolicy> Order(IQueryable<RoutingPolicy> q) => q.OrderBy(p => p.Priority);
    protected override RoutingPolicyDto Map(RoutingPolicy e) => e.ToDto();

    protected override void Apply(RoutingPolicy e, UpsertRoutingPolicyRequest r)
    {
        e.Priority = r.Priority;
        e.Source = Clean(r.Source);
        e.Destination = Clean(r.Destination);
        e.InInterface = Clean(r.InInterface);
        e.Table = r.Table;
        e.IsEnabled = r.IsEnabled;
        e.Description = Clean(r.Description);
    }

    protected override async Task EnsureUniqueAsync(RoutingPolicy e, CancellationToken ct)
    {
        if (await Uow.Policies.AnyAsync(x => x.GatewayId == e.GatewayId && x.Priority == e.Priority && x.Id != e.Id, ct))
            throw new ConflictException($"Priority {e.Priority} is already used by another routing policy.");
    }
}

// ---------------------------------------------------------------------------------------------- NAT
public interface INatRuleService : IGatewayChildService<NatRuleDto, UpsertNatRuleRequest>;

public sealed class NatRuleService(INetworkUnitOfWork uow)
    : GatewayChildService<NatRule, NatRuleDto, UpsertNatRuleRequest>(uow), INatRuleService
{
    protected override IRepository<NatRule> Repository => Uow.NatRules;
    protected override string ItemName => "NAT rule";
    protected override IOrderedQueryable<NatRule> Order(IQueryable<NatRule> q) => q.OrderBy(n => n.Priority).ThenBy(n => n.Name);
    protected override NatRuleDto Map(NatRule e) => e.ToDto();

    protected override void Apply(NatRule e, UpsertNatRuleRequest r)
    {
        e.Name = r.Name.Trim();
        e.Type = r.Type;
        e.Protocol = r.Protocol;
        e.InInterface = Clean(r.InInterface);
        e.OutInterface = Clean(r.OutInterface);
        e.Source = Clean(r.Source);
        e.Destination = Clean(r.Destination);
        e.DestinationPort = Clean(r.DestinationPort);
        e.TranslatedAddress = Clean(r.TranslatedAddress);
        e.TranslatedPort = r.TranslatedPort;
        e.Priority = r.Priority;
        e.IsEnabled = r.IsEnabled;
        e.Description = Clean(r.Description);
    }
}

// ---------------------------------------------------------------------------------------------- VPN tunnels
public interface ITunnelService : IGatewayChildService<TunnelDto, UpsertTunnelRequest>;

public sealed class TunnelService(INetworkUnitOfWork uow)
    : GatewayChildService<VpnTunnel, TunnelDto, UpsertTunnelRequest>(uow), ITunnelService
{
    protected override IRepository<VpnTunnel> Repository => Uow.Tunnels;
    protected override string ItemName => "VPN tunnel";
    protected override IOrderedQueryable<VpnTunnel> Order(IQueryable<VpnTunnel> q) => q.OrderBy(t => t.Name);
    protected override TunnelDto Map(VpnTunnel e) => e.ToDto();

    protected override void Apply(VpnTunnel e, UpsertTunnelRequest r)
    {
        e.Name = r.Name.Trim();
        e.Type = r.Type;
        e.IsEnabled = r.IsEnabled;
        e.Description = Clean(r.Description);
        var wg = r.Type == Domain.Enums.VpnType.WireGuard;
        // Keep only the fields relevant to the tunnel type.
        e.InterfaceName = wg ? Clean(r.InterfaceName) : null;
        e.ListenPort = wg ? r.ListenPort : null;
        e.TunnelAddress = wg ? Clean(r.TunnelAddress) : null;
        e.PublicKey = wg ? Clean(r.PublicKey) : null;
        e.Mtu = wg ? r.Mtu : null;
        e.RemoteGateway = wg ? null : Clean(r.RemoteGateway);
        e.LocalSubnets = wg ? null : Clean(r.LocalSubnets);
        e.RemoteSubnets = wg ? null : Clean(r.RemoteSubnets);
        e.IkeVersion = r.IkeVersion;
        e.Proposals = wg ? null : Clean(r.Proposals)?.ToLowerInvariant();
    }

    protected override async Task EnsureUniqueAsync(VpnTunnel e, CancellationToken ct)
    {
        if (await Uow.Tunnels.AnyAsync(x => x.GatewayId == e.GatewayId && x.Name == e.Name && x.Id != e.Id, ct))
            throw new ConflictException($"A tunnel named '{e.Name}' already exists on this gateway.");
        if (e.InterfaceName is { } ifName && await Uow.Tunnels.AnyAsync(x => x.GatewayId == e.GatewayId && x.InterfaceName == ifName && x.Id != e.Id, ct))
            throw new ConflictException($"Interface '{ifName}' is already used by another tunnel.");
    }
}
