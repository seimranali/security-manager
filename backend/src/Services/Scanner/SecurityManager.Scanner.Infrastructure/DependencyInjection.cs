using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Scanner.Application.Abstractions;
using SecurityManager.Scanner.Infrastructure.BackgroundJobs;
using SecurityManager.Scanner.Infrastructure.Integration;
using SecurityManager.Scanner.Infrastructure.Persistence;
using SecurityManager.Scanner.Infrastructure.Scanning;
using SecurityManager.Shared.Infrastructure.Security;

namespace SecurityManager.Scanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScannerInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(ScannerOptions.Section);
        services.Configure<ScannerOptions>(section);
        var options = section.Get<ScannerOptions>() ?? new ScannerOptions();

        services.AddServiceDbContext<ScannerDbContext>(config, "ScannerDb", "scanner", "SecurityManager_Scanner");
        services.AddScoped<IScannerUnitOfWork, ScannerUnitOfWork>();

        services.AddSingleton<IPortScanner, TcpPortScanner>();
        services.AddSingleton<ITlsProbe, TlsCertificateProbe>();
        services.AddHttpClient<IHttpProbe, HttpHeaderProbe>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("SecurityManager-Scanner/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

        services.AddHttpClient<IAlertPublisher, HttpAlertPublisher>(c =>
        {
            c.BaseAddress = new Uri(config["Services:Alert"] ?? "http://localhost:5104/");
            c.Timeout = TimeSpan.FromSeconds(5);
        }).AddServiceAuthentication("scanner");

        services.AddSingleton<IScanQueue, ChannelScanQueue>();
        services.AddHostedService<ScanWorker>();
        services.AddHostedService<ScanSchedulerWorker>();

        return services;
    }
}
