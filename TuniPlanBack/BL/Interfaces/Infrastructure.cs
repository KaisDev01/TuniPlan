using Entities;
using Entities.Enums;

namespace BL.Interfaces;

/// <summary>The authenticated caller (implemented in the API from HttpContext).</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    string? IpAddress { get; }
    string? UserAgent { get; }
    Guid RequireUserId();
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
}

/// <summary>Encrypts secrets stored in the database (TOTP keys). Implemented with ASP.NET Data Protection.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

/// <summary>Cache of security stamps used to validate access tokens; invalidated when the stamp changes.</summary>
public interface IUserSessionCache
{
    void Invalidate(Guid userId);
}

public sealed record PaymentInit(string? CheckoutUrl, string ProviderReference);

public interface IPaymentGateway
{
    PaymentProvider Provider { get; }
    Task<PaymentInit> InitiateAsync(Guid paymentId, decimal amount, string description, CancellationToken ct = default);
    Task<bool> RefundAsync(string providerReference, decimal amount, CancellationToken ct = default);
}

public static class Roles
{
    public const string Client = "Client";
    public const string Business = "Business";
    public const string Admin = "Admin";

    public static IEnumerable<string> From(AccountRoles roles)
    {
        if (roles.HasFlag(AccountRoles.Client)) yield return Client;
        if (roles.HasFlag(AccountRoles.Business)) yield return Business;
        if (roles.HasFlag(AccountRoles.Admin)) yield return Admin;
    }
}

/// <summary>Identity confirmed by Google or Facebook.</summary>
public sealed record ExternalIdentity(ExternalProvider Provider, string ProviderKey, string? Email, bool EmailVerified, string? FirstName, string? LastName);

/// <summary>Checks a Google ID token / Facebook access token with the provider (implemented in the API).</summary>
public interface IExternalIdentityVerifier
{
    bool IsConfigured(ExternalProvider provider);
    /// <returns>Null when the token is invalid, expired or issued for another app.</returns>
    Task<ExternalIdentity?> VerifyAsync(ExternalProvider provider, string token, CancellationToken ct = default);
}
