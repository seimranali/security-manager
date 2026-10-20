using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Scanner.Application.Services;

namespace SecurityManager.Scanner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddScannerApplication(this IServiceCollection services)
    {
        services.AddScoped<ITargetService, TargetService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IScanJobService, ScanJobService>();
        services.AddScoped<IFindingService, FindingService>();
        services.AddScoped<IScanExecutor, ScanExecutor>();
        services.AddScoped<IScheduleDispatcher, ScheduleDispatcher>();
        return services;
    }
}
