using System.ComponentModel.DataAnnotations;

namespace DTOs.Account;

public sealed record MembershipDto(Guid OrganizationId, string Name, string Slug, string Role);

public sealed record UserProfileDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = default!;
    public string LastName { get; init; } = default!;
    public string? Email { get; init; }
    public string PhoneNumber { get; init; } = default!;
    public bool PhoneConfirmed { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public string PreferredLanguage { get; init; } = "fr";
    public string? AvatarUrl { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public IReadOnlyList<MembershipDto> Organizations { get; init; } = [];
}

public sealed record UpdateProfileRequest
{
    [Required, StringLength(80, MinimumLength = 2)] public string FirstName { get; init; } = default!;
    [Required, StringLength(80, MinimumLength = 2)] public string LastName { get; init; } = default!;
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
    [RegularExpression("^(fr|ar|en)$")] public string PreferredLanguage { get; init; } = "fr";
}

public sealed record NotificationSettingsDto
{
    public bool PushEnabled { get; init; }
    public bool SmsEnabled { get; init; }
    public bool WhatsAppEnabled { get; init; }
    public bool EmailEnabled { get; init; }
    public bool Reminder24h { get; init; }
    public bool Reminder2h { get; init; }
}

public sealed record FamilyMemberDto(Guid Id, string FirstName, string LastName, string Relation, string? PhoneNumber);

public sealed record UpsertFamilyMemberRequest
{
    [Required, StringLength(80)] public string FirstName { get; init; } = default!;
    [Required, StringLength(80)] public string LastName { get; init; } = default!;
    [Required, StringLength(40)] public string Relation { get; init; } = default!;
    [StringLength(20)] public string? PhoneNumber { get; init; }
}

public sealed record DeleteAccountRequest
{
    [Required] public string Password { get; init; } = default!;
}

/// <summary>POST /api/account/devices: call it after login and whenever Expo gives a new push token.</summary>
public sealed record RegisterDeviceRequest
{
    /// <summary>Expo push token, e.g. "ExponentPushToken[xxxxxxxx]".</summary>
    [Required, StringLength(200, MinimumLength = 10)] public string Token { get; init; } = default!;
    /// <summary>ios | android | web</summary>
    [RegularExpression("^(ios|android|web)$")] public string? Platform { get; init; }
    [StringLength(200)] public string? DeviceName { get; init; }
}

public sealed record DeviceDto(Guid Id, string? Platform, string? DeviceName, DateTime CreatedAt, DateTime LastSeenAt);
