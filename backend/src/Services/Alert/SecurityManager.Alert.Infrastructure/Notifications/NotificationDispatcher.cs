using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.DTOs;

namespace SecurityManager.Alert.Infrastructure.Notifications;

/// <summary>In-memory queue between alert creation and delivery. Bounded so a dead mail server cannot exhaust memory.</summary>
public sealed class NotificationQueue(ILogger<NotificationQueue> logger) : INotificationQueue
{
    private readonly Channel<(AlertDto Alert, bool Escalation)> _channel =
        Channel.CreateBounded<(AlertDto, bool)>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<(AlertDto Alert, bool Escalation)> Reader => _channel.Reader;

    public void Enqueue(AlertDto alert, bool escalation)
    {
        if (!_channel.Writer.TryWrite((alert, escalation))) logger.LogWarning("Notification queue is full; dropped alert {Title}", alert.Title);
    }
}

/// <summary>Delivers queued alerts to every enabled channel whose severity threshold they meet (one retry per channel).</summary>
public sealed class NotificationDispatcher(NotificationQueue queue, IServiceScopeFactory scopes, ILogger<NotificationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (alert, escalation) in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await DispatchAsync(alert, escalation, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Notification dispatch failed for {Title}", alert.Title); }
        }
    }

    private async Task DispatchAsync(AlertDto alert, bool escalation, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IAlertUnitOfWork>();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();

        var channels = await uow.Channels.ListAsync(c => c.IsEnabled, ct: ct);
        foreach (var channel in channels.Where(c => c.Accepts(alert.Severity) && (!escalation || c.NotifyOnEscalation)))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await sender.SendAsync(channel, alert, escalation, ct);
                    channel.LastSentAt = DateTime.UtcNow;
                    channel.SentCount++;
                    channel.LastError = null;
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        continue;
                    }
                    channel.LastFailureAt = DateTime.UtcNow;
                    channel.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    logger.LogWarning("Notification channel {Channel} failed: {Message}", channel.Name, ex.Message);
                    break;
                }
            }
        }
        await uow.SaveChangesAsync(ct);
    }
}
