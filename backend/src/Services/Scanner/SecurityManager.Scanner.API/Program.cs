using SecurityManager.Scanner.Application;
using SecurityManager.Scanner.Infrastructure;
using SecurityManager.Scanner.Infrastructure.Persistence;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Shared.Web;

// Content root next to the executable: required when running as a Windows service (which starts in System32).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.AddServiceDefaults().AddDbContextCheck<ScannerDbContext>("database");
builder.Services
    .AddScannerApplication()
    .AddScannerInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync<ScannerDbContext>(ScannerSeeder.SeedAsync);

app.UseServiceDefaults();
app.Run();
