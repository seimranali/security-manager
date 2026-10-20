using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Network.Application.Abstractions;
using SecurityManager.Network.Infrastructure.Persistence;

namespace SecurityManager.Network.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNetworkInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Gateways load several collections at once; split queries avoid a cartesian explosion.
        services.AddServiceDbContext<NetworkDbContext>(config, "NetworkDb", "network", "SecurityManager_Network");
        services.AddScoped<INetworkUnitOfWork, NetworkUnitOfWork>();
        return services;
    }
}
