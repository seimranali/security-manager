using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Application.Services;
using SecurityManager.Shared.Kernel.Security;
using SecurityManager.Shared.Web;

namespace SecurityManager.Identity.API.Controllers;

[ApiController]
[Route("api/auth/users")]
[Authorize(Policy = Authentication.AdminPolicy)]
public sealed class UsersController(IUserService users, IAuthService auth, IMfaService mfa) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<UserDto>> GetAll(CancellationToken ct) => users.GetAllAsync(ct);

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await users.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<UserDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, ActingUserId(), ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ActingUserId(), ct);
        return NoContent();
    }

    /// <summary>Signs the user out everywhere by revoking all refresh tokens.</summary>
    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<ActionResult<object>> RevokeSessions(Guid id, CancellationToken ct) =>
        Ok(new { revoked = await users.RevokeSessionsAsync(id, ct) });

    /// <summary>Removes a user's authenticator (lost device) and signs them out everywhere.</summary>
    [HttpPost("{id:guid}/reset-mfa")]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct)
    {
        await mfa.ResetAsync(id, ct);
        return NoContent();
    }

    /// <summary>Sign-in audit log for all users (or one user).</summary>
    [HttpGet("login-history")]
    public Task<IReadOnlyList<LoginAttemptDto>> LoginHistory([FromQuery] Guid? userId, [FromQuery] int take = 200, CancellationToken ct = default) =>
        auth.GetLoginHistoryAsync(userId, take, ct);

    private Guid ActingUserId() => Guid.Parse(User.FindFirst(AppClaims.Subject)!.Value);
}
