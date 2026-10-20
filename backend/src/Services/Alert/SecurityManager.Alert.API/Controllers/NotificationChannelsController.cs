using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecurityManager.Alert.Application.Services;
using SecurityManager.Shared.Web;

namespace SecurityManager.Alert.API.Controllers;

/// <summary>Email and webhook destinations for alert notifications (administrators only: they hold credentials).</summary>
[ApiController]
[Route("api/alerts/channels")]
[Authorize(Policy = Authentication.AdminPolicy)]
public sealed class NotificationChannelsController(INotificationChannelService channels) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationChannelDto>> GetAll(CancellationToken ct) => channels.GetAllAsync(ct);

    [HttpPost]
    public async Task<ActionResult<NotificationChannelDto>> Create(SaveNotificationChannelRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await channels.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<NotificationChannelDto> Update(Guid id, SaveNotificationChannelRequest request, CancellationToken ct) =>
        channels.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await channels.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public Task<NotificationChannelDto> Test(Guid id, CancellationToken ct) => channels.TestAsync(id, ct);
}
