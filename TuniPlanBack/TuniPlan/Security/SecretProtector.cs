using BL.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace TuniPlan.Security;

/// <summary>Encrypts TOTP secrets at rest with ASP.NET Core Data Protection.</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("TuniPlan.TwoFactorSecrets.v1");
    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
