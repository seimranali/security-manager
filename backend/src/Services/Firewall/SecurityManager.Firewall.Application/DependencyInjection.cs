using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Firewall.Application.Services;

namespace SecurityManager.Firewall.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddFirewallApplication(this IServiceCollection services)
    {
        services.AddScoped<IPolicyService, PolicyService>();
        services.AddScoped<IRuleService, RuleService>();
        services.AddScoped<ITrafficService, TrafficService>();
        services.AddScoped<IIpListService, IpListService>();
        services.AddScoped<IEnforcementService, EnforcementService>();
        services.AddScoped<IThreatFeedService, ThreatFeedService>();
        services.AddScoped<IConfigBackupService, ConfigBackupService>();
        return services;
    }
}
