using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SecurityManager.Shared.Infrastructure.Security;
using SecurityManager.Shared.Kernel.Security;

namespace SecurityManager.Shared.Web;

public static class Authentication
{
    public const string WritePolicy = "CanWrite";
    public const string AdminPolicy = "AdminOnly";

    /// <summary>
    /// JWT bearer validation for every service. All endpoints require an authenticated user by default
    /// (fallback policy); state-changing requests additionally require a writer role (see <see cref="WriteAccessFilter"/>).
    /// </summary>
    public static void AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddJwtTokenFactory(builder.Configuration);
        var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenFactory.CreateKey(jwt.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaims.Name,
                    RoleClaimType = AppClaims.Role
                };
                o.Events = new JwtBearerEvents
                {
                    // Browsers cannot set headers on WebSocket connections; SignalR sends the token in the query string.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        if (ctx.AuthenticateFailure is SecurityTokenExpiredException)
                            ctx.Response.Headers.Append("X-Token-Expired", "true");
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(WritePolicy, p => p.RequireRole(Roles.Writers))
            .AddPolicy(AdminPolicy, p => p.RequireRole(Roles.Admin));
    }
}

/// <summary>Lets any authenticated role call a state-changing action (e.g. changing one's own password).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowAnyRoleWriteAttribute : Attribute;

/// <summary>Global MVC filter: non-GET requests require Admin, Analyst or Service role; Viewers are read-only.</summary>
public sealed class WriteAccessFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
            return Task.CompletedTask;

        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() || metadata.OfType<AllowAnyRoleWriteAttribute>().Any())
            return Task.CompletedTask;

        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true && !Roles.Writers.Any(user.IsInRole))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = "Your role has read-only access."
            }) { StatusCode = StatusCodes.Status403Forbidden };
        }
        return Task.CompletedTask;
    }
}
