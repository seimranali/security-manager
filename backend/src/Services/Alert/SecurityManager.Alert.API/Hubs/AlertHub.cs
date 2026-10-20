using Microsoft.AspNetCore.SignalR;
using SecurityManager.Alert.Application.Abstractions;
using SecurityManager.Alert.Application.DTOs;

namespace SecurityManager.Alert.API.Hubs;

/// <summary>Clients subscribe here to receive "alertCreated" / "alertUpdated" pushes.</summary>
public sealed class AlertHub : Hub;

public sealed class SignalRAlertNotifier(IHubContext<AlertHub> hub) : IAlertNotifier
{
    public Task AlertCreatedAsync(AlertDto alert, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("alertCreated", alert, ct);

    public Task AlertUpdatedAsync(AlertDto alert, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync("alertUpdated", alert, ct);
}
