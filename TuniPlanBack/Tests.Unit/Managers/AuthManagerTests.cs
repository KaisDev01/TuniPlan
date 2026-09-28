using BL.Managers;
using BL.Options;
using Common.Exceptions;
using Common.Helpers;
using Common.Security;
using DAL.Context;
using DAO;
using BL.Interfaces;
using DTOs.Auth;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Tests.Unit.Fakes;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tests.Unit.Managers;

public class AuthManagerTests : IAsyncLifetime
{
    private TuniPlanDbContext _db = default!;
    private AuthManager _auth = default!;
    private readonly CapturingSms _sms = new();
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeSessionCache _sessions = new();
    private readonly FakeExternalVerifier _external = new();

    public Task InitializeAsync()
    {
        _db = TestDb.Create();
        var uow = new UnitOfWork(_db);
        _auth = new AuthManager(uow, new Pbkdf2PasswordHasher(), new FakeTokenService(), new FakeProtector(), _sessions, _sms,
            _currentUser, new AuditManager(uow, _currentUser), _external, new NullLoggerManager(), new SystemClock(),
            MsOptions.Create(new JwtOptions { SigningKey = new string('k', 40) }),
            MsOptions.Create(new SecurityOptions { ExposeDevCodes = true, VerificationCodeResendSeconds = 0 }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static RegisterRequest Register(AccountType type = AccountType.Client) => new()
    {
        AccountType = type, FirstName = "Amine", LastName = "Ben Salah", PhoneNumber = "98 765 432",
        Email = "Amine@Example.com", Password = "MonMotDePasse2026"
    };

    private async Task<AuthResponse> RegisterAndVerifyAsync(AccountType type = AccountType.Client)
    {
        var reg = await _auth.RegisterAsync(Register(type));
        return await _auth.VerifyPhoneAsync(new VerifyPhoneRequest { PhoneNumber = "98765432", Code = reg.DevCode!, DeviceName = "test" });
    }

    [Fact]
    public async Task Register_sends_code_and_verify_logs_in()
    {
        var reg = await _auth.RegisterAsync(Register(AccountType.Business));
        Assert.True(reg.RequiresPhoneVerification);
        Assert.Single(_sms.Sent);
        Assert.Equal(reg.DevCode, _sms.LastCode());

        var auth = await _auth.VerifyPhoneAsync(new VerifyPhoneRequest { PhoneNumber = "+21698765432", Code = reg.DevCode! });
        Assert.True(auth.User.PhoneConfirmed);
        Assert.Contains("Business", auth.User.Roles);
        Assert.Equal("amine@example.com", auth.User.Email);
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));

        // Only the hash of the refresh token is stored
        Assert.False(await _db.RefreshTokens.AnyAsync(t => t.TokenHash == auth.RefreshToken));
    }

    [Fact]
    public async Task Duplicate_phone_is_rejected()
    {
        await _auth.RegisterAsync(Register());
        await Assert.ThrowsAsync<ConflictException>(() => _auth.RegisterAsync(Register() with { Email = null }));
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _auth.RegisterAsync(Register() with { Password = "12345678" }));
    }

    [Fact]
    public async Task Wrong_code_is_rejected_and_counted()
    {
        await _auth.RegisterAsync(Register());
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _auth.VerifyPhoneAsync(new VerifyPhoneRequest { PhoneNumber = "98765432", Code = "000000" == _sms.LastCode() ? "111111" : "000000" }));
        Assert.Equal(1, (await _db.VerificationCodes.SingleAsync(c => c.ConsumedAt == null)).Attempts);
    }

    [Fact]
    public async Task Login_with_email_or_phone()
    {
        await RegisterAndVerifyAsync();
        var byEmail = await _auth.LoginAsync(new LoginRequest { Identifier = "amine@example.com", Password = "MonMotDePasse2026" });
        var byPhone = await _auth.LoginAsync(new LoginRequest { Identifier = "98 765 432", Password = "MonMotDePasse2026" });
        Assert.NotNull(byEmail.Auth);
        Assert.NotNull(byPhone.Auth);
    }

    [Fact]
    public async Task Account_is_locked_after_five_failed_logins()
    {
        await RegisterAndVerifyAsync();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                _auth.LoginAsync(new LoginRequest { Identifier = "98765432", Password = "Mauvais123" }));

        // Even the right password is refused while locked
        await Assert.ThrowsAsync<AccountLockedException>(() =>
            _auth.LoginAsync(new LoginRequest { Identifier = "98765432", Password = "MonMotDePasse2026" }));
    }

    [Fact]
    public async Task Unknown_user_gets_generic_error()
    {
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.LoginAsync(new LoginRequest { Identifier = "inconnu@example.com", Password = "MonMotDePasse2026" }));
        Assert.Equal("invalid_credentials", ex.Code);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_session()
    {
        var first = await RegisterAndVerifyAsync();
        var second = await _auth.RefreshAsync(new RefreshRequest { RefreshToken = first.RefreshToken });
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // Reusing the old (rotated) token = theft suspected → whole family revoked
        await Assert.ThrowsAsync<UnauthorizedException>(() => _auth.RefreshAsync(new RefreshRequest { RefreshToken = first.RefreshToken }));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _auth.RefreshAsync(new RefreshRequest { RefreshToken = second.RefreshToken }));
    }

    [Fact]
    public async Task Two_factor_login_requires_totp_code()
    {
        var auth = await RegisterAndVerifyAsync();
        _currentUser.UserId = auth.User.Id;
        var setup = await _auth.SetupTwoFactorAsync();
        var code = Totp.Compute(Base32.Decode(setup.Secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        await _auth.EnableTwoFactorAsync(new TwoFactorCodeRequest { Code = code });
        _currentUser.UserId = null;

        var login = await _auth.LoginAsync(new LoginRequest { Identifier = "98765432", Password = "MonMotDePasse2026" });
        Assert.True(login.RequiresTwoFactor);
        Assert.Null(login.Auth);

        var tokens = await _auth.LoginTwoFactorAsync(new TwoFactorLoginRequest { ChallengeId = login.ChallengeId!.Value, Code = code });
        Assert.True(tokens.User.TwoFactorEnabled);
    }

    [Fact]
    public async Task Password_reset_revokes_sessions_and_changes_stamp()
    {
        var auth = await RegisterAndVerifyAsync();
        var stampBefore = (await _db.Users.SingleAsync()).SecurityStamp;
        var devCode = await _auth.ForgotPasswordAsync(new ForgotPasswordRequest { Identifier = "98765432" });

        await _auth.ResetPasswordAsync(new ResetPasswordRequest { PhoneNumber = "98765432", Code = devCode!, NewPassword = "NouveauMdp2026" });

        Assert.NotEqual(stampBefore, (await _db.Users.SingleAsync()).SecurityStamp);
        Assert.Contains(auth.User.Id, _sessions.Invalidated);
        await Assert.ThrowsAsync<UnauthorizedException>(() => _auth.RefreshAsync(new RefreshRequest { RefreshToken = auth.RefreshToken }));
        Assert.NotNull((await _auth.LoginAsync(new LoginRequest { Identifier = "98765432", Password = "NouveauMdp2026" })).Auth);
    }

    [Fact]
    public async Task Password_reset_accepts_email_identifier()
    {
        await RegisterAndVerifyAsync();
        var devCode = await _auth.ForgotPasswordAsync(new ForgotPasswordRequest { Identifier = "amine@example.com" });

        await _auth.ResetPasswordAsync(new ResetPasswordRequest { Identifier = "Amine@Example.com", Code = devCode!, NewPassword = "NouveauMdp2026" });

        Assert.NotNull((await _auth.LoginAsync(new LoginRequest { Identifier = "amine@example.com", Password = "NouveauMdp2026" })).Auth);
    }

    [Fact]
    public async Task Dates_are_read_back_as_utc()
    {
        await RegisterAndVerifyAsync();
        _db.ChangeTracker.Clear();

        var user = await _db.Users.SingleAsync();

        Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
    }

    private const string GoogleToken = "google-id-token-for-tests";

    [Fact]
    public async Task External_login_creates_account_after_phone_verification()
    {
        _external.Identities[GoogleToken] = new ExternalIdentity(ExternalProvider.Google, "g-123", "nour@example.com", true, "Nour", "Trabelsi");

        var first = await _auth.ExternalLoginAsync(new ExternalLoginRequest { Provider = ExternalProvider.Google, Token = GoogleToken });
        Assert.True(first.RequiresPhoneNumber);

        var second = await _auth.ExternalLoginAsync(new ExternalLoginRequest { Provider = ExternalProvider.Google, Token = GoogleToken, PhoneNumber = "22 333 444" });
        Assert.True(second.RequiresPhoneVerification);
        var auth = await _auth.VerifyPhoneAsync(new VerifyPhoneRequest { PhoneNumber = "22333444", Code = second.DevCode! });
        Assert.Equal("Nour", auth.User.FirstName);

        // Next time: logged in directly with the linked Google identity
        var again = await _auth.ExternalLoginAsync(new ExternalLoginRequest { Provider = ExternalProvider.Google, Token = GoogleToken });
        Assert.NotNull(again.Auth);
    }

    [Fact]
    public async Task External_login_links_existing_account_with_same_verified_email()
    {
        var existing = await RegisterAndVerifyAsync();
        _external.Identities[GoogleToken] = new ExternalIdentity(ExternalProvider.Google, "g-456", "amine@example.com", true, "Amine", "B");

        var login = await _auth.ExternalLoginAsync(new ExternalLoginRequest { Provider = ExternalProvider.Google, Token = GoogleToken });

        Assert.Equal(existing.User.Id, login.Auth!.User.Id);
        Assert.Equal(1, await _db.ExternalLogins.CountAsync());
    }

    [Fact]
    public async Task External_login_rejects_invalid_token()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _auth.ExternalLoginAsync(new ExternalLoginRequest { Provider = ExternalProvider.Facebook, Token = "not-a-valid-token-value" }));
    }
}
