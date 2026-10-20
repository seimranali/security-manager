using System.Text.Json.Serialization;
using SecurityManager.Alert.API.Hubs;
using SecurityManager.Alert.Application;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Infrastructure;
using SecurityManager.Alert.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Web;

// Content root next to the executable: required when running as a Windows service (which starts in System32).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.AddServiceDefaults().AddDbContextCheck<AlertDbContext>("database");
builder.Services
    .AddAlertApplication()
    .AddAlertInfrastructure(builder.Configuration);

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IAlertNotifier, SignalRAlertNotifier>();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync<AlertDbContext>(AlertSchema.UpgradeAsync);

app.UseServiceDefaults();
app.MapHub<AlertHub>("/hubs/alerts").RequireCors(ServiceDefaults.CorsPolicy);
app.Run();
