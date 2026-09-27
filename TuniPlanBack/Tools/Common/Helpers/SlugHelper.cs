using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Common.Helpers;

public static partial class SlugHelper
{
    public static string Slugify(string text)
    {
        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var slug = Invalid().Replace(sb.ToString().Normalize(NormalizationForm.FormC), "-");
        slug = Dashes().Replace(slug, "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "entreprise" : slug[..Math.Min(slug.Length, 80)];
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Invalid();

    [GeneratedRegex("-{2,}")]
    private static partial Regex Dashes();
}
