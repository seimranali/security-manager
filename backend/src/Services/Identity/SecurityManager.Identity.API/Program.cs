using Microsoft.AspNetCore.HttpOverrides;
using SecurityManager.Identity.Application;
using SecurityManager.Identity.Application.Abstractions;
using SecurityManager.Identity.Infrastructure;
using SecurityManager.Identity.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Web;

// Content root next to the executable: required when running as a Windows service (which starts in System32).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.AddServiceDefaults().AddDbContextCheck<IdentityDbContext>("database");
builder.Services
    .AddIdentityApplication()
    .AddIdentityInfrastructure(builder.Configuration);

// The gateway (loopback) forwards the real client IP, used for session auditing.
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync(IdentitySeeder.Create(
    app.Services.GetRequiredService<IPasswordHasher>(), app.Configuration));

app.UseForwardedHeaders();
app.UseServiceDefaults();
app.Run();
