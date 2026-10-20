using SecurityManager.Firewall.Application;
using SecurityManager.Firewall.Infrastructure;
using SecurityManager.Firewall.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Web;

// Content root next to the executable: required when running as a Windows service (which starts in System32).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.AddServiceDefaults().AddDbContextCheck<FirewallDbContext>("database");
builder.Services
    .AddFirewallApplication()
    .AddFirewallInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync<FirewallDbContext>(FirewallSeeder.SeedAsync);

app.UseServiceDefaults();
app.Run();
