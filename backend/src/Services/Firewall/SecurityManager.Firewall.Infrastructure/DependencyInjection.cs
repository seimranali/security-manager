using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Firewall.Application.Abstractions;
using SecurityManager.Firewall.Application.Services;
using SecurityManager.Firewall.Infrastructure.Enforcement;
using SecurityManager.Firewall.Infrastructure.Feeds;
using SecurityManager.Firewall.Infrastructure.Integration;
using SecurityManager.Firewall.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Security;

namespace SecurityManager.Firewall.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFirewallInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddServiceDbContext<FirewallDbContext>(config, "FirewallDb", "firewall", "SecurityManager_Firewall");

        services.AddScoped<IFirewallUnitOfWork, FirewallUnitOfWork>();

        services.AddHttpClient<ISecurityEventPublisher, HttpSecurityEventPublisher>(c =>
        {
            c.BaseAddress = new Uri(config["Services:Security"] ?? "http://localhost:5102/");
            c.Timeout = TimeSpan.FromSeconds(5);
        }).AddServiceAuthentication("firewall");

        // Threat-intel / country feeds: downloaded on their own schedule by a background worker.
        services.AddHttpClient<IFeedDownloader, HttpFeedDownloader>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(60);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SecurityManager/1.0 (+feed sync)");
        });
        services.AddHostedService<FeedSyncWorker>();

        // Built-in enforcement: applies the rules to this machine's own firewall (Windows Firewall / nftables).
        // Needs administrator / root rights; no separate agent is required on the Security Manager host.
        services.AddLocalEnforcement(config);

        return services;
    }
}
