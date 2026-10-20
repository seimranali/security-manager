using SecurityManager.Security.Application;
using SecurityManager.Security.Infrastructure;
using SecurityManager.Security.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Web;

// Content root next to the executable: required when running as a Windows service (which starts in System32).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.AddServiceDefaults().AddDbContextCheck<SecurityDbContext>("database");
builder.Services
    .AddSecurityApplication()
    .AddSecurityInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync<SecurityDbContext>(SecuritySeeder.SeedAsync);

app.UseServiceDefaults();
app.Run();
