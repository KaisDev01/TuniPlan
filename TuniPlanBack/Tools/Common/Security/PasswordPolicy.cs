namespace Common.Security;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "password", "password1", "azerty123", "azertyuiop",
        "qwerty123", "motdepasse", "11111111", "00000000", "tunisie123", "iloveyou", "admin123"
    };

    /// <summary>Returns a list of French error messages (empty = valid).</summary>
    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            errors.Add($"Le mot de passe doit contenir au moins {MinLength} caractères.");
        else
        {
            if (!password.Any(char.IsLetter)) errors.Add("Le mot de passe doit contenir au moins une lettre.");
            if (!password.Any(char.IsDigit)) errors.Add("Le mot de passe doit contenir au moins un chiffre.");
            if (password.Length > 128) errors.Add("Le mot de passe est trop long (128 caractères max).");
            if (CommonPasswords.Contains(password)) errors.Add("Ce mot de passe est trop courant.");
        }
        return errors;
    }
}
