using SecurityManager.Network.Application;
using SecurityManager.Network.Infrastructure;
using SecurityManager.Network.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Web;

// Content root next to the executable: required when running as a Windows service (which starts in System32).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.AddServiceDefaults().AddDbContextCheck<NetworkDbContext>("database");
builder.Services
    .AddNetworkApplication()
    .AddNetworkInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync<NetworkDbContext>();

app.UseServiceDefaults();
app.Run();
