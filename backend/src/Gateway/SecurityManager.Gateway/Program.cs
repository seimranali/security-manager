using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Windows services start in System32; resolve wwwroot/appsettings next to the executable.
    ContentRootPath = AppContext.BaseDirectory
});
builder.Host.UseWindowsService();
builder.Host.UseSystemd();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// Brute-force protection for credential endpoints: 10 requests per minute per client IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHttpClient("health", c => c.Timeout = TimeSpan.FromSeconds(3));

var app = builder.Build();

// Browser hardening for everything served by the gateway (UI and API).
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    // The UI loads nothing from third parties (fonts are self-hosted), so the policy can be strict.
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
    await next();
});

app.UseCors();
app.UseRateLimiter();

// Serve the built React UI (wwwroot) when present, so the whole app runs on one origin.
var hasUi = File.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "index.html"));
if (hasUi)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            // Hashed assets can be cached forever; index.html must always be revalidated.
            ctx.Context.Response.Headers.CacheControl = ctx.File.Name == "index.html"
                ? "no-cache"
                : "public, max-age=31536000, immutable";
        }
    });
}

// Aggregated health of all downstream microservices, consumed by the dashboard.
app.MapGet("/api/system/health", async (IHttpClientFactory factory, IConfiguration config, CancellationToken ct) =>
{
    var client = factory.CreateClient("health");
    var services = config.GetSection("ReverseProxy:Clusters").GetChildren()
        .Select(c => (Name: c.Key, Address: c["Destinations:primary:Address"]!));

    var checks = await Task.WhenAll(services.Select(async s =>
    {
        var started = DateTime.UtcNow;
        try
        {
            using var response = await client.GetAsync(new Uri(new Uri(s.Address), "health"), ct);
            return new ServiceHealth(s.Name, response.IsSuccessStatusCode ? "Healthy" : "Unhealthy",
                (int)(DateTime.UtcNow - started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ServiceHealth(s.Name, "Unreachable", null);
        }
    }));

    return Results.Ok(checks);
});

app.MapReverseProxy();

// Client-side routes (e.g. /network/gateways/123) fall back to the SPA; API paths never do.
if (hasUi)
{
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath!, "index.html"));
    });
}

app.Run();

internal sealed record ServiceHealth(string Service, string Status, int? LatencyMs);
