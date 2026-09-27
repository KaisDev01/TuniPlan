namespace Common.Helpers;

public static class TunisiaReference
{
    public static readonly IReadOnlyList<string> Governorates =
    [
        "Ariana", "Béja", "Ben Arous", "Bizerte", "Gabès", "Gafsa", "Jendouba", "Kairouan",
        "Kasserine", "Kébili", "Le Kef", "Mahdia", "La Manouba", "Médenine", "Monastir", "Nabeul",
        "Sfax", "Sidi Bouzid", "Siliana", "Sousse", "Tataouine", "Tozeur", "Tunis", "Zaghouan"
    ];

    public static bool IsGovernorate(string value) =>
        Governorates.Any(g => string.Equals(g, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public const string DefaultTimeZone = "Africa/Tunis";
}
