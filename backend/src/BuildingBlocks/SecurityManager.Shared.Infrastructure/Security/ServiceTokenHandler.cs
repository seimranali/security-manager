using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.Shared.Infrastructure.Security;

/// <summary>Attaches a short-lived service JWT (role "Service") to outgoing service-to-service calls.</summary>
public sealed class ServiceTokenHandler(IJwtTokenFactory factory, IOptions<JwtOptions> options, string serviceName)
    : DelegatingHandler
{
    private IssuedToken? _current;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_current is null || _current.ExpiresAt <= DateTime.UtcNow.AddMinutes(1))
        {
            _current = factory.Create(
            [
                new Claim(AppClaims.Subject, $"service:{serviceName}"),
                new Claim(AppClaims.Name, serviceName),
                new Claim(AppClaims.Role, Roles.Service)
            ], TimeSpan.FromMinutes(options.Value.ServiceTokenMinutes));
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _current.Token);
        return base.SendAsync(request, ct);
    }
}

public static class SecurityRegistration
{
    /// <summary>Registers <see cref="JwtOptions"/> (validated at startup) and the token factory.</summary>
    public static IServiceCollection AddJwtTokenFactory(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.Section))
            .Validate(o => System.Text.Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey must be at least 32 bytes.")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 60, "Jwt:AccessTokenMinutes must be between 1 and 60.")
            .ValidateOnStart();
        services.AddSingleton<IJwtTokenFactory, JwtTokenFactory>();
        return services;
    }

    /// <summary>Authenticates an HttpClient's requests with a service token.</summary>
    public static IHttpClientBuilder AddServiceAuthentication(this IHttpClientBuilder builder, string serviceName) =>
        builder.AddHttpMessageHandler(sp => new ServiceTokenHandler(
            sp.GetRequiredService<IJwtTokenFactory>(),
            sp.GetRequiredService<IOptions<JwtOptions>>(),
            serviceName));
}
