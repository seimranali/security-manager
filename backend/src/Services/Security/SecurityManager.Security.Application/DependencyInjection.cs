using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Security.Application.Services;

namespace SecurityManager.Security.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSecurityApplication(this IServiceCollection services)
    {
        services.AddScoped<IDetectionEngine, DetectionEngine>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IDetectionService, DetectionService>();
        services.AddScoped<IDetectionRuleService, DetectionRuleService>();
        services.AddScoped<ISecurityStatsService, SecurityStatsService>();
        return services;
    }
}
