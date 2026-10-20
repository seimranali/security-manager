using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecurityManager.Identity.Application.DTOs;
using SecurityManager.Identity.Application.Services;
using SecurityManager.Shared.Kernel.Exceptions;
using SecurityManager.Shared.Kernel.Security;
using SecurityManager.Shared.Web;

namespace SecurityManager.Identity.API.Controllers;

/// <summary>
/// Issues short-lived JWT access tokens (returned in the body, kept in memory by the SPA) and rotating
/// refresh tokens (HttpOnly, Secure, SameSite=Strict cookie scoped to /api/auth — never readable by JavaScript).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth, IMfaService mfa, IConfiguration config) : ControllerBase
{
    public const string RefreshCookie = "sm_refresh";
    private const string CookiePath = "/api/auth";

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await auth.LoginAsync(request, Client(), ct);
            return Respond(result);
        }
        catch (MfaRequiredException)
        {
            // Password accepted; the client must resubmit with an authenticator code.
            return new AuthResponse(null, null, null, null, MfaRequired: true);
        }
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<AuthResponse> Refresh(CancellationToken ct)
    {
        try
        {
            var result = await auth.RefreshAsync(Request.Cookies[RefreshCookie], Client(), ct);
            return Respond(result);
        }
        catch (UnauthorizedException)
        {
            ClearCookie();
            throw;
        }
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct);
        ClearCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public Task<UserDto> Me(CancellationToken ct) => auth.GetMeAsync(CurrentUserId(), ct);

    [HttpGet("sessions")]
    public Task<IReadOnlyList<SessionDto>> Sessions(CancellationToken ct) => auth.GetSessionsAsync(CurrentUserId(), ct);

    /// <summary>The signed-in user's recent sign-in attempts.</summary>
    [HttpGet("login-history")]
    public Task<IReadOnlyList<LoginAttemptDto>> LoginHistory([FromQuery] int take = 50, CancellationToken ct = default) =>
        auth.GetLoginHistoryAsync(CurrentUserId(), take, ct);

    // ---- Multi-factor authentication (self-service)

    [HttpPost("mfa/setup")]
    [AllowAnyRoleWrite]
    public Task<MfaSetupDto> MfaSetup(CancellationToken ct) => mfa.BeginSetupAsync(CurrentUserId(), ct);

    [HttpPost("mfa/enable")]
    [AllowAnyRoleWrite]
    public Task<MfaEnabledDto> MfaEnable(MfaCodeRequest request, CancellationToken ct) => mfa.EnableAsync(CurrentUserId(), request.Code, ct);

    [HttpPost("mfa/disable")]
    [AllowAnyRoleWrite]
    public async Task<IActionResult> MfaDisable(MfaDisableRequest request, CancellationToken ct)
    {
        await mfa.DisableAsync(CurrentUserId(), request, ct);
        return NoContent();
    }

    /// <summary>Changes the password, signs out all other sessions and issues a fresh session for this client.</summary>
    [HttpPost("change-password")]
    [AllowAnyRoleWrite]
    public async Task<AuthResponse> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await auth.ChangePasswordAsync(CurrentUserId(), request, Client(), ct);
        return Respond(result);
    }

    private AuthResponse Respond(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = config.GetValue("Auth:SecureCookies", true),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = result.RefreshTokenExpiresAt,
            IsEssential = true
        });
        Response.Headers.CacheControl = "no-store";
        return new AuthResponse(result.AccessToken, result.AccessTokenExpiresAt, "Bearer", result.User);
    }

    private void ClearCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath, Secure = config.GetValue("Auth:SecureCookies", true), SameSite = SameSiteMode.Strict, HttpOnly = true });

    private Guid CurrentUserId() =>
        Guid.TryParse(User.FindFirst(AppClaims.Subject)?.Value, out var id) ? id : throw new UnauthorizedException("Invalid token subject.");

    private ClientInfo Client() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
