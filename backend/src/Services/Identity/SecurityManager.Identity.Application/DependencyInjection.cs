using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Identity.Application.Services;

namespace SecurityManager.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IMfaService, MfaService>();
        return services;
    }
}
