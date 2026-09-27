using System.Text;
using Common.Helpers;
using Common.Security;

namespace Tests.Unit.Tools;

public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_succeeds()
    {
        var hash = _hasher.Hash("MonMotDePasse2026");
        Assert.True(_hasher.Verify("MonMotDePasse2026", hash));
        Assert.False(_hasher.Verify("mauvais", hash));
    }

    [Fact]
    public void Same_password_gives_different_hashes()
    {
        Assert.NotEqual(_hasher.Hash("abc12345"), _hasher.Hash("abc12345"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v1.x.y.z")]
    public void Verify_rejects_malformed_hash(string stored) => Assert.False(_hasher.Verify("abc12345", stored));

    [Theory]
    [InlineData("court1", false)]
    [InlineData("12345678", false)]
    [InlineData("seulementdeslettres", false)]
    [InlineData("TuniPlan2026", true)]
    public void Password_policy(string password, bool valid) => Assert.Equal(valid, PasswordPolicy.Validate(password).Count == 0);
}

public class TotpTests
{
    [Fact]
    public void Matches_rfc6238_test_vector()
    {
        // RFC 6238, SHA-1, T = 59s → 94287082 (8 digits) → last 6 digits 287082
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        Assert.Equal("287082", Totp.Compute(key, 1));
    }

    [Fact]
    public void Verify_accepts_current_code_and_rejects_wrong_one()
    {
        var secret = Totp.GenerateSecret();
        var now = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
        var step = new DateTimeOffset(now).ToUnixTimeSeconds() / 30;
        var code = Totp.Compute(Base32.Decode(secret), step);
        Assert.True(Totp.Verify(secret, code, now));
        Assert.True(Totp.Verify(secret, code, now.AddSeconds(30)));   // clock drift tolerated
        Assert.False(Totp.Verify(secret, code, now.AddMinutes(5)));
        Assert.False(Totp.Verify(secret, "000000" == code ? "111111" : "000000", now));
    }

    [Fact]
    public void Base32_round_trip()
    {
        var data = Encoding.UTF8.GetBytes("TuniPlan secret");
        Assert.Equal(data, Base32.Decode(Base32.Encode(data)));
    }
}

public class HelperTests
{
    [Theory]
    [InlineData("98 123 456", "+21698123456")]
    [InlineData("+216 98123456", "+21698123456")]
    [InlineData("0021622333444", "+21622333444")]
    [InlineData("71 000 000", "+21671000000")]
    [InlineData("12345678", null)]
    [InlineData("981234", null)]
    [InlineData("", null)]
    public void Normalizes_tunisian_numbers(string input, string? expected) =>
        Assert.Equal(expected, PhoneNumberHelper.NormalizeTunisian(input));

    [Theory]
    [InlineData("Maison Lilia", "maison-lilia")]
    [InlineData("Cabinet Dr. Olfa Gharbi", "cabinet-dr-olfa-gharbi")]
    [InlineData("Espace Beauté Ennasr", "espace-beaute-ennasr")]
    [InlineData("!!!", "entreprise")]
    public void Slugify(string input, string expected) => Assert.Equal(expected, SlugHelper.Slugify(input));

    [Fact]
    public void Numeric_code_has_requested_length() => Assert.Matches("^[0-9]{6}$", SecureRandom.NumericCode(6));
}
