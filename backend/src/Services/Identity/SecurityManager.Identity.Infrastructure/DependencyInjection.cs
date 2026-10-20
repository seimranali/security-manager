using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Infrastructure.BackgroundJobs;
using SecurityManager.Identity.Infrastructure.Integration;
using SecurityManager.Identity.Infrastructure.Persistence;
using SecurityManager.Identity.Infrastructure.Security;
using SecurityManager.Shared.Infrastructure.Security;

namespace SecurityManager.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddServiceDbContext<IdentityDbContext>(config, "IdentityDb", "identity", "SecurityManager_Identity");
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISecretProtector, AesSecretProtector>();
        services.AddHttpClient<ISecurityEventSink, HttpSecurityEventSink>(c =>
        {
            c.BaseAddress = new Uri(config["Services:Security"] ?? "http://localhost:5102/");
            c.Timeout = TimeSpan.FromSeconds(3);
        }).AddServiceAuthentication("identity");
        services.AddHostedService<RefreshTokenCleanupWorker>();
        return services;
    }
}
