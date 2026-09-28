using Entities.Base;
using Entities.Enums;

namespace Entities;

public class User : BaseEntity, ISoftDelete
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    /// <summary>E.164 format, e.g. +21698123456.</summary>
    public string PhoneNumber { get; set; } = default!;
    public bool PhoneConfirmed { get; set; }

    public string PasswordHash { get; set; } = default!;
    /// <summary>Changes on password change / logout-all: every access token carrying the old stamp is rejected.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public AccountRoles Roles { get; set; } = AccountRoles.Client;
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public bool TwoFactorEnabled { get; set; }
    /// <summary>TOTP secret, encrypted with ASP.NET Data Protection.</summary>
    public string? TwoFactorSecretProtected { get; set; }

    public string PreferredLanguage { get; set; } = "fr";
    public string? AvatarUrl { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public NotificationSettings NotificationSettings { get; set; } = new();

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<OrganizationMember> Memberships { get; set; } = new List<OrganizationMember>();
    public ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}

/// <summary>Owned type (stored in the Users table).</summary>
public class NotificationSettings
{
    public bool PushEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = true;
    public bool WhatsAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool Reminder24h { get; set; } = true;
    public bool Reminder2h { get; set; } = true;
}

public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    /// <summary>SHA-256 of the token: the raw token is never stored.</summary>
    public string TokenHash { get; set; } = default!;
    /// <summary>All tokens of one login session share a family; reuse of a revoked token revokes the whole family.</summary>
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? CreatedByIp { get; set; }
    public string? DeviceName { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}

public class VerificationCode : BaseEntity
{
    public Guid? UserId { get; set; }
    /// <summary>Phone or email the code was sent to.</summary>
    public string Target { get; set; } = default!;
    public VerificationPurpose Purpose { get; set; }
    public string? CodeHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class FamilyMember : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string Relation { get; set; } = default!;
    public string? PhoneNumber { get; set; }
}

public class Favorite : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
}

public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = default!;
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>Expo push token of one phone / browser of the user (POST /api/account/devices).</summary>
public class UserDevice : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    /// <summary>"ExponentPushToken[...]" — unique: a token moves to the last account logged in on the device.</summary>
    public string Token { get; set; } = default!;
    /// <summary>ios | android | web</summary>
    public string? Platform { get; set; }
    public string? DeviceName { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Google / Facebook identity linked to an account (POST /api/auth/external).</summary>
public class ExternalLogin : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public ExternalProvider Provider { get; set; }
    /// <summary>Stable user id at the provider ("sub" for Google, user id for Facebook).</summary>
    public string ProviderKey { get; set; } = default!;
    public string? Email { get; set; }
}
