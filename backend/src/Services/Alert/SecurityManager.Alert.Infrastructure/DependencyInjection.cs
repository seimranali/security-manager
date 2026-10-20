using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecurityManager.Shared.Infrastructure.Persistence;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.Services;
using SecurityManager.Alert.Infrastructure.Notifications;
using SecurityManager.Alert.Infrastructure.Persistence;

namespace SecurityManager.Alert.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAlertInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddServiceDbContext<AlertDbContext>(config, "AlertDb", "alert", "SecurityManager_Alert");
        services.AddScoped<IAlertUnitOfWork, AlertUnitOfWork>();

        // Email / webhook notifications: alerts are queued and delivered by a background dispatcher.
        services.AddSingleton<NotificationQueue>();
        services.AddSingleton<INotificationQueue>(sp => sp.GetRequiredService<NotificationQueue>());
        services.AddHostedService<NotificationDispatcher>();
        services.AddSingleton<IChannelSecretProtector, ChannelSecretProtector>();
        services.AddHttpClient<INotificationSender, NotificationSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
        return services;
    }
}
