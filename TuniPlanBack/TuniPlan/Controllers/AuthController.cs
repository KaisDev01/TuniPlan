using BL.Managers;
using DTOs.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TuniPlan.Infrastructure;

namespace TuniPlan.Controllers;

/// <summary>Registration (client or business), phone verification, login, 2FA, refresh tokens, passwords.</summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(ServiceCollectionExtensions.AuthRateLimit)]
public sealed class AuthController(IAuthManager auth) : ControllerBase
{
    /// <summary>Create an account. AccountType = Client or Business. An SMS code is sent to verify the phone.</summary>
    [AllowAnonymous, HttpPost("register")]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await auth.RegisterAsync(request, ct));

    /// <summary>Confirm the phone with the SMS code. Returns tokens (the user is logged in).</summary>
    [AllowAnonymous, HttpPost("verify-phone")]
    public async Task<ActionResult<AuthResponse>> VerifyPhone(VerifyPhoneRequest request, CancellationToken ct) =>
        Ok(await auth.VerifyPhoneAsync(request, ct));

    [AllowAnonymous, HttpPost("resend-code")]
    public async Task<ActionResult<RegisterResponse>> ResendCode(ResendCodeRequest request, CancellationToken ct) =>
        Ok(await auth.ResendCodeAsync(request, ct));

    /// <summary>Login with email or phone + password. May answer RequiresTwoFactor or RequiresPhoneVerification.</summary>
    [AllowAnonymous, HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));

    [AllowAnonymous, HttpPost("login/2fa")]
    public async Task<ActionResult<AuthResponse>> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginTwoFactorAsync(request, ct));

    /// <summary>Exchange a refresh token for a new pair (rotation; reuse of an old token revokes the session).</summary>
    [AllowAnonymous, HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct) =>
        Ok(await auth.RefreshAsync(request, ct));

    /// <summary>Always answers 202 (no account enumeration).</summary>
    [AllowAnonymous, HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var devCode = await auth.ForgotPasswordAsync(request, ct);
        return Accepted(new { message = "Si un compte existe, un code a été envoyé par SMS.", devCode });
    }

    [AllowAnonymous, HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<AuthResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        Ok(await auth.ChangePasswordAsync(request, ct));

    /// <summary>Active sessions (devices) of the current user.</summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct) => Ok(await auth.GetSessionsAsync(ct));

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        await auth.RevokeSessionAsync(sessionId, ct);
        return NoContent();
    }

    /// <summary>Start 2FA setup: returns the secret and the otpauth:// URI to show as a QR code.</summary>
    [HttpPost("2fa/setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> SetupTwoFactor(CancellationToken ct) => Ok(await auth.SetupTwoFactorAsync(ct));

    [HttpPost("2fa/enable")]
    public async Task<IActionResult> EnableTwoFactor(TwoFactorCodeRequest request, CancellationToken ct)
    {
        await auth.EnableTwoFactorAsync(request, ct);
        return NoContent();
    }

    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor(TwoFactorCodeRequest request, CancellationToken ct)
    {
        await auth.DisableTwoFactorAsync(request, ct);
        return NoContent();
    }
}
