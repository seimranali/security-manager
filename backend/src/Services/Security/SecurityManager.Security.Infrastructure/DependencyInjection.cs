using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Security.Application.Abstractions;
using SecurityManager.Security.Infrastructure.Integration;
using SecurityManager.Security.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Security;

namespace SecurityManager.Security.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSecurityInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddServiceDbContext<SecurityDbContext>(config, "SecurityDb", "security", "SecurityManager_Security");

        services.AddScoped<ISecurityUnitOfWork, SecurityUnitOfWork>();

        services.AddHttpClient<IAlertPublisher, HttpAlertPublisher>(c =>
        {
            c.BaseAddress = new Uri(config["Services:Alert"] ?? "http://localhost:5104/");
            c.Timeout = TimeSpan.FromSeconds(5);
        }).AddServiceAuthentication("security");

        services.Configure<AutoBlockOptions>(config.GetSection(AutoBlockOptions.Section));
        services.AddHttpClient<IFirewallBlocker, HttpFirewallBlocker>(c =>
        {
            c.BaseAddress = new Uri(config["Services:Firewall"] ?? "http://localhost:5101/");
            c.Timeout = TimeSpan.FromSeconds(5);
        }).AddServiceAuthentication("security");

        return services;
    }
}
