using System.Security.Cryptography;
using System.Text;

namespace Common.Security;

public static class SecureRandom
{
    /// <summary>URL-safe random token (default 64 bytes = 512 bits).</summary>
    public static string Token(int bytes = 64) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>Numeric code with uniform distribution (no modulo bias).</summary>
    public static string NumericCode(int digits = 6)
    {
        var sb = new StringBuilder(digits);
        for (var i = 0; i < digits; i++) sb.Append(RandomNumberGenerator.GetInt32(0, 10));
        return sb.ToString();
    }

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool FixedTimeEqualsHex(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    public static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
