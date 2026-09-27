namespace BL.Options;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "TuniPlan";
    public string Audience { get; set; } = "TuniPlan.Clients";
    /// <summary>At least 32 random characters. NEVER commit the real key: use user-secrets or an environment variable.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class SecurityOptions
{
    public const string Section = "Security";
    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int VerificationCodeMinutes { get; set; } = 10;
    public int VerificationCodeMaxAttempts { get; set; } = 5;
    public int VerificationCodeResendSeconds { get; set; } = 60;
    public int VerificationCodesPerHour { get; set; } = 5;
    public int MfaChallengeMinutes { get; set; } = 5;
    /// <summary>Development only: returns SMS codes in API responses so you can test without an SMS provider.</summary>
    public bool ExposeDevCodes { get; set; }
    /// <summary>Allows /payments/mock/confirm (development / demo).</summary>
    public bool EnableMockPayments { get; set; }
}

public sealed class AppOptions
{
    public const string Section = "App";
    /// <summary>Public web front-end URL, used for business public links (e.g. https://tuniplan.tn).</summary>
    public string PublicWebUrl { get; set; } = "http://localhost:8081";
    public int MaxPendingRequestsPerClientPerOrganization { get; set; } = 3;
    public int BookingHorizonDays { get; set; } = 90;
}
