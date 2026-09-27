using System.Text.RegularExpressions;

namespace Common.Helpers;

public static partial class PhoneNumberHelper
{
    /// <summary>
    /// Normalizes a Tunisian number to E.164 (+216XXXXXXXX). Accepts "98 123 456", "0021698123456", "+216 98123456".
    /// Returns null when the number is not valid.
    /// </summary>
    public static string? NormalizeTunisian(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = NonDigits().Replace(input, "");
        if (digits.StartsWith("00216")) digits = digits[5..];
        else if (digits.StartsWith("216") && digits.Length == 11) digits = digits[3..];
        if (digits.Length != 8) return null;
        // Tunisian mobile/landline prefixes: 2x,3x,4x,5x,7x,9x
        if (!"234579".Contains(digits[0])) return null;
        return "+216" + digits;
    }

    public static bool LooksLikeEmail(string input) => input.Contains('@');

    public static string Mask(string phone) =>
        phone.Length <= 4 ? phone : new string('•', phone.Length - 3) + phone[^3..];

    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();
}
