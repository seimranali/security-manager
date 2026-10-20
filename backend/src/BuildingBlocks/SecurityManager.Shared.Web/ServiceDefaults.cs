using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace SecurityManager.Shared.Web;

public static class ServiceDefaults
{
    public const string CorsPolicy = "frontend";

    /// <summary>Registers controllers, problem details, OpenAPI, CORS, JWT authentication and health checks.</summary>
    public static IHealthChecksBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        // Both are no-ops when run normally: they integrate with the Windows Service Control Manager (and Event Log)
        // or with systemd (sd_notify + journald) only when started as a service on that platform.
        builder.Host.UseWindowsService();
        builder.Host.UseSystemd();

        builder.Services
            .AddControllers(o => o.Filters.Add<WriteAccessFilter>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddOpenApi();
        builder.AddJwtAuthentication();

        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                      ?? ["http://localhost:5173"];
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        return builder.Services.AddHealthChecks();
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.UseCors(CorsPolicy);
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous();
        }

        app.MapControllers();
        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }
}
