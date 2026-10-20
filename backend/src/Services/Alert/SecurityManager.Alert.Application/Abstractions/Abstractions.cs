using SecurityManager.Alert.Application.DTOs;
using SecurityManager.Alert.Domain.Entities;
using SecurityManager.Shared.Kernel.Abstractions;

namespace SecurityManager.Alert.Application.Abstractions;

public interface IAlertUnitOfWork : IUnitOfWork
{
    IRepository<SecurityAlert> Alerts { get; }
    IRepository<NotificationChannel> Channels { get; }
}

/// <summary>Hands alerts to the background notification dispatcher (email / webhooks) without blocking the caller.</summary>
public interface INotificationQueue
{
    void Enqueue(AlertDto alert, bool escalation);
}

/// <summary>Delivers one notification through a channel. Throws on failure (the dispatcher records the error).</summary>
public interface INotificationSender
{
    Task SendAsync(NotificationChannel channel, AlertDto alert, bool escalation, CancellationToken ct = default);
}

/// <summary>Pushes alert changes to connected clients in real time.</summary>
public interface IAlertNotifier
{
    Task AlertCreatedAsync(AlertDto alert, CancellationToken ct = default);
    Task AlertUpdatedAsync(AlertDto alert, CancellationToken ct = default);
}
