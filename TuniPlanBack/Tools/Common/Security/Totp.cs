using System.Security.Cryptography;
using System.Text;

namespace Common.Security;

/// <summary>RFC 6238 TOTP (Google Authenticator, Microsoft Authenticator, Authy…).</summary>
public static class Totp
{
    private const int Digits = 6;
    private const int StepSeconds = 30;

    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static string BuildOtpAuthUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={Digits}&period={StepSeconds}";

    /// <summary>Accepts the current step and ±1 step (clock drift).</summary>
    public static bool Verify(string base32Secret, string code, DateTime utcNow, int window = 1)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != Digits || !code.All(char.IsDigit)) return false;
        var key = Base32.Decode(base32Secret);
        var step = new DateTimeOffset(utcNow, TimeSpan.Zero).ToUnixTimeSeconds() / StepSeconds;
        for (var i = -window; i <= window; i++)
        {
            var expected = Compute(key, step + i);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)))
                return true;
        }
        return false;
    }

    public static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xFF); step >>= 8; }
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % (int)Math.Pow(10, Digits)).ToString().PadLeft(Digits, '0');
    }
}

public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decode(string input)
    {
        var clean = input.Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var val = Alphabet.IndexOf(c);
            if (val < 0) throw new FormatException("Invalid base32 character.");
            buffer = (buffer << 5) | val;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
