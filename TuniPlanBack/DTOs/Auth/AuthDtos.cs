using System.ComponentModel.DataAnnotations;

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

public enum CodePurpose { VerifyPhone = 0, ResetPassword = 2 }

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
    [Required] public string PhoneNumber { get; init; } = default!;
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
