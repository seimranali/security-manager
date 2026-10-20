using Microsoft.AspNetCore.Mvc;
using SecurityManager.Network.Application.DTOs;
using SecurityManager.Network.Application.Services;

namespace SecurityManager.Network.API.Controllers;

[ApiController]
[Route("api/network/gateways")]
public sealed class GatewaysController(IGatewayService gateways) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<GatewayDto>> GetAll(CancellationToken ct) => gateways.GetAllAsync(ct);

    /// <summary>Gateway with all configuration items and validation issues.</summary>
    [HttpGet("{id:guid}")]
    public Task<GatewayDetailDto> Get(Guid id, CancellationToken ct) => gateways.GetDetailAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<GatewayDto>> Create(UpsertGatewayRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await gateways.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<GatewayDto> Update(Guid id, UpsertGatewayRequest request, CancellationToken ct) => gateways.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await gateways.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Generated Linux configuration files (iproute2, nftables, WireGuard, strongSwan, keepalived).</summary>
    [HttpGet("{id:guid}/config")]
    public Task<GeneratedConfigDto> Config(Guid id, CancellationToken ct) => gateways.GenerateConfigAsync(id, ct);

    /// <summary>All generated files as a zip archive.</summary>
    [HttpGet("{id:guid}/config/bundle")]
    public async Task<IActionResult> Bundle(Guid id, CancellationToken ct)
    {
        var (fileName, content) = await gateways.GenerateBundleAsync(id, ct);
        return File(content, "application/zip", fileName);
    }

    /// <summary>Records that the current revision has been applied to the device.</summary>
    [HttpPost("{id:guid}/mark-deployed")]
    public Task<GatewayDto> MarkDeployed(Guid id, CancellationToken ct) => gateways.MarkDeployedAsync(id, User.Identity?.Name, ct);
}

/// <summary>Shared CRUD endpoints for configuration items nested under a gateway.</summary>
[ApiController]
public abstract class GatewayChildController<TDto, TRequest>(IGatewayChildService<TDto, TRequest> service) : ControllerBase
    where TRequest : class
{
    [HttpGet]
    public Task<IReadOnlyList<TDto>> List(Guid gatewayId, CancellationToken ct) => service.ListAsync(gatewayId, ct);

    [HttpPost]
    public async Task<ActionResult<TDto>> Create(Guid gatewayId, TRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await service.CreateAsync(gatewayId, request, ct));

    [HttpPut("{id:guid}")]
    public Task<TDto> Update(Guid gatewayId, Guid id, TRequest request, CancellationToken ct) =>
        service.UpdateAsync(gatewayId, id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid gatewayId, Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(gatewayId, id, ct);
        return NoContent();
    }
}

[Route("api/network/gateways/{gatewayId:guid}/interfaces")]
public sealed class InterfacesController(IInterfaceService s) : GatewayChildController<InterfaceDto, UpsertInterfaceRequest>(s);

[Route("api/network/gateways/{gatewayId:guid}/routes")]
public sealed class RoutesController(IRouteService s) : GatewayChildController<RouteDto, UpsertRouteRequest>(s);

[Route("api/network/gateways/{gatewayId:guid}/policies")]
public sealed class RoutingPoliciesController(IRoutingPolicyService s) : GatewayChildController<RoutingPolicyDto, UpsertRoutingPolicyRequest>(s);

[Route("api/network/gateways/{gatewayId:guid}/nat")]
public sealed class NatRulesController(INatRuleService s) : GatewayChildController<NatRuleDto, UpsertNatRuleRequest>(s);

[Route("api/network/gateways/{gatewayId:guid}/vpn")]
public sealed class TunnelsController(ITunnelService s) : GatewayChildController<TunnelDto, UpsertTunnelRequest>(s);

[ApiController]
[Route("api/network/gateways/{gatewayId:guid}/vpn/{tunnelId:guid}/peers")]
public sealed class PeersController(IPeerService peers) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PeerDto>> Create(Guid gatewayId, Guid tunnelId, UpsertPeerRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await peers.CreateAsync(gatewayId, tunnelId, request, ct));

    [HttpPut("{id:guid}")]
    public Task<PeerDto> Update(Guid gatewayId, Guid tunnelId, Guid id, UpsertPeerRequest request, CancellationToken ct) =>
        peers.UpdateAsync(gatewayId, tunnelId, id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid gatewayId, Guid tunnelId, Guid id, CancellationToken ct)
    {
        await peers.DeleteAsync(gatewayId, tunnelId, id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/network/clusters")]
public sealed class ClustersController(IClusterService clusters) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ClusterDto>> GetAll(CancellationToken ct) => clusters.GetAllAsync(ct);

    [HttpPost]
    public async Task<ActionResult<ClusterDto>> Create(UpsertClusterRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await clusters.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<ClusterDto> Update(Guid id, UpsertClusterRequest request, CancellationToken ct) => clusters.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await clusters.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{clusterId:guid}/virtual-ips")]
    public async Task<ActionResult<VirtualIpDto>> AddVip(Guid clusterId, UpsertVirtualIpRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await clusters.AddVirtualIpAsync(clusterId, request, ct));

    [HttpPut("{clusterId:guid}/virtual-ips/{id:guid}")]
    public Task<VirtualIpDto> UpdateVip(Guid clusterId, Guid id, UpsertVirtualIpRequest request, CancellationToken ct) =>
        clusters.UpdateVirtualIpAsync(clusterId, id, request, ct);

    [HttpDelete("{clusterId:guid}/virtual-ips/{id:guid}")]
    public async Task<IActionResult> DeleteVip(Guid clusterId, Guid id, CancellationToken ct)
    {
        await clusters.DeleteVirtualIpAsync(clusterId, id, ct);
        return NoContent();
    }
}
