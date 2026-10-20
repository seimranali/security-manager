using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Alert.Application.Services;

namespace SecurityManager.Alert.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAlertApplication(this IServiceCollection services)
    {
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<INotificationChannelService, NotificationChannelService>();
        return services;
    }
}
