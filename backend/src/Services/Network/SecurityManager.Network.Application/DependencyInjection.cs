using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Network.Application.Services;

namespace SecurityManager.Network.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNetworkApplication(this IServiceCollection services)
    {
        services.AddScoped<GatewayLoader>();
        services.AddScoped<IGatewayService, GatewayService>();
        services.AddScoped<IInterfaceService, InterfaceService>();
        services.AddScoped<IRouteService, RouteService>();
        services.AddScoped<IRoutingPolicyService, RoutingPolicyService>();
        services.AddScoped<INatRuleService, NatRuleService>();
        services.AddScoped<ITunnelService, TunnelService>();
        services.AddScoped<IPeerService, PeerService>();
        services.AddScoped<IClusterService, ClusterService>();
        return services;
    }
}
