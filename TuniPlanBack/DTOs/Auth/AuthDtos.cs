using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Entities.Enums;

namespace DTOs.Auth;

public enum AccountType { Client = 0, Business = 1 }

public sealed record RegisterRequest
{
    [Required] public AccountType AccountType { get; init; }
    [Required, StringLength(80, MinimumLength = 2)] public string FirstName { get; init; } = default!;
    [Required, StringLength(80, MinimumLength = 2)] public string LastName { get; init; } = default!;
    /// <summary>Tunisian number, any format ("98 123 456", "+21698123456").</summary>
    [Required, StringLength(20)] public string PhoneNumber { get; init; } = default!;
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; init; } = default!;
    [StringLength(5)] public string? PreferredLanguage { get; init; }
}

public sealed record RegisterResponse(Guid UserId, string PhoneNumberMasked, bool RequiresPhoneVerification, string? DevCode);

public sealed record VerifyPhoneRequest
{
    [Required] public string PhoneNumber { get; init; } = default!;
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; init; } = default!;
    [StringLength(200)] public string? DeviceName { get; init; }
}

[Description("Why an SMS code is (re)sent by /api/auth/resend-code.")]
public enum CodePurpose
{
    [Description("Confirm the phone number after /register (then call /verify-phone).")] VerifyPhone = 0,
    [Description("Reset a forgotten password (then call /reset-password).")] ResetPassword = 2
}

public sealed record ResendCodeRequest
{
    [Required] public string PhoneNumber { get; init; } = default!;
    public CodePurpose Purpose { get; init; } = CodePurpose.VerifyPhone;
}

public sealed record LoginRequest
{
    /// <summary>Email or phone number.</summary>
    [Required, StringLength(256)] public string Identifier { get; init; } = default!;
    [Required, StringLength(128)] public string Password { get; init; } = default!;
    [StringLength(200)] public string? DeviceName { get; init; }
}

public sealed record LoginResponse
{
    public AuthResponse? Auth { get; init; }
    public bool RequiresTwoFactor { get; init; }
    public Guid? ChallengeId { get; init; }
    public bool RequiresPhoneVerification { get; init; }
    public string? PhoneNumberMasked { get; init; }
    /// <summary>Development only (Security:ExposeDevCodes): the SMS code sent for phone verification.</summary>
    public string? DevCode { get; init; }
    /// <summary>/auth/external only: first Google / Facebook login, ask the phone number and call again with phoneNumber.</summary>
    public bool RequiresPhoneNumber { get; init; }
}

public sealed record TwoFactorLoginRequest
{
    [Required] public Guid ChallengeId { get; init; }
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; init; } = default!;
    [StringLength(200)] public string? DeviceName { get; init; }
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    Account.UserProfileDto User);

public sealed record RefreshRequest
{
    [Required, StringLength(200)] public string RefreshToken { get; init; } = default!;
    [StringLength(200)] public string? DeviceName { get; init; }
}

public sealed record LogoutRequest
{
    public string? RefreshToken { get; init; }
    public bool AllDevices { get; init; }
}

public sealed record ForgotPasswordRequest
{
    [Required, StringLength(256)] public string Identifier { get; init; } = default!;
}

public sealed record ResetPasswordRequest
{
    /// <summary>Email or phone number (same value as in /forgot-password).</summary>
    [StringLength(256)] public string? Identifier { get; init; }
    /// <summary>Deprecated: use <see cref="Identifier"/>. Still accepted for older clients.</summary>
    [StringLength(20)] public string? PhoneNumber { get; init; }
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; init; } = default!;
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; init; } = default!;
}

public sealed record ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; init; } = default!;
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; init; } = default!;
}

public sealed record TwoFactorSetupResponse(string Secret, string OtpAuthUri);

public sealed record TwoFactorCodeRequest
{
    [Required, StringLength(6, MinimumLength = 6)] public string Code { get; init; } = default!;
}

public sealed record SessionDto(Guid Id, string? DeviceName, string? IpAddress, DateTime CreatedAt, DateTime ExpiresAt);

/// <summary>
/// POST /api/auth/external. Flow: 1) send the provider token; 2) if requiresPhoneNumber, ask the phone and send again with phoneNumber;
/// 3) if requiresPhoneVerification, call /verify-phone with the SMS code; otherwise the tokens are in auth.
/// </summary>
public sealed record ExternalLoginRequest
{
    [Required] public ExternalProvider Provider { get; init; }
    /// <summary>Google: ID token (JWT). Facebook: user access token.</summary>
    [Required, StringLength(4096, MinimumLength = 20)] public string Token { get; init; } = default!;
    /// <summary>Only for the first login (account creation): Tunisian phone number, verified by SMS.</summary>
    [StringLength(20)] public string? PhoneNumber { get; init; }
    /// <summary>Only for account creation.</summary>
    public AccountType AccountType { get; init; } = AccountType.Client;
    [StringLength(200)] public string? DeviceName { get; init; }
}
