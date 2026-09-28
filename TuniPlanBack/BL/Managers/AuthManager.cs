using BL.Interfaces;
using BL.Mapping;
using BL.Options;
using Common.Exceptions;
using Common.Helpers;
using Common.Security;
using DAO.Interfaces;
using DTOs.Account;
using DTOs.Auth;
using Entities;
using Entities.Enums;
using LoggerService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService;

namespace BL.Managers;

public interface IAuthManager
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> VerifyPhoneAsync(VerifyPhoneRequest request, CancellationToken ct = default);
    Task<RegisterResponse> ResendCodeAsync(ResendCodeRequest request, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginTwoFactorAsync(TwoFactorLoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct = default);
    Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
    Task<TwoFactorSetupResponse> SetupTwoFactorAsync(CancellationToken ct = default);
    Task EnableTwoFactorAsync(TwoFactorCodeRequest request, CancellationToken ct = default);
    Task DisableTwoFactorAsync(TwoFactorCodeRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken ct = default);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed class AuthManager(
    IUnitOfWork uow,
    IPasswordHasher hasher,
    ITokenService tokens,
    ISecretProtector protector,
    IUserSessionCache sessionCache,
    ISmsSender sms,
    ICurrentUser currentUser,
    IAuditManager audit,
    ILoggerManager logger,
    IClock clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<SecurityOptions> securityOptions) : IAuthManager
{
    private readonly JwtOptions _jwt = jwtOptions.Value;
    private readonly SecurityOptions _sec = securityOptions.Value;
    private const string InvalidCredentials = "Identifiant ou mot de passe incorrect.";

    // ------------------------------------------------------------ Registration
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var phone = PhoneNumberHelper.NormalizeTunisian(request.PhoneNumber)
                    ?? throw ValidationException.For(nameof(request.PhoneNumber), "Numéro de téléphone tunisien invalide (8 chiffres).");
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();

        var passwordErrors = PasswordPolicy.Validate(request.Password);
        if (passwordErrors.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]> { [nameof(request.Password)] = passwordErrors.ToArray() });

        if (await uow.Users.PhoneExistsAsync(phone, ct)) throw new ConflictException("Ce numéro est déjà associé à un compte.", "phone_taken");
        if (email is not null && await uow.Users.EmailExistsAsync(email, ct)) throw new ConflictException("Cet email est déjà associé à un compte.", "email_taken");

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = phone,
            Email = email,
            PasswordHash = hasher.Hash(request.Password),
            Roles = request.AccountType == AccountType.Business ? AccountRoles.Client | AccountRoles.Business : AccountRoles.Client,
            PreferredLanguage = request.PreferredLanguage is "ar" or "en" ? request.PreferredLanguage : "fr"
        };
        await uow.Users.AddAsync(user, ct);
        var code = await CreateCodeAsync(phone, VerificationPurpose.VerifyPhone, user.Id, ct);
        await audit.AddAsync("register", user.Id, request.AccountType.ToString(), ct);
        await uow.SaveChangesAsync(ct);
        await SendCodeSmsAsync(phone, code, VerificationPurpose.VerifyPhone, ct);

        return new RegisterResponse(user.Id, PhoneNumberHelper.Mask(phone), true, _sec.ExposeDevCodes ? code : null);
    }

    public async Task<AuthResponse> VerifyPhoneAsync(VerifyPhoneRequest request, CancellationToken ct = default)
    {
        var phone = NormalizePhoneOrThrow(request.PhoneNumber);
        var user = await uow.Users.GetByPhoneAsync(phone, ct) ?? throw new BadRequestException("Code invalide ou expiré.", "invalid_code");
        await ConsumeCodeAsync(phone, VerificationPurpose.VerifyPhone, request.Code, ct);
        user.PhoneConfirmed = true;
        await audit.AddAsync("phone_verified", user.Id, null, ct);
        return await IssueTokensAsync(user, request.DeviceName, null, ct);
    }

    public async Task<RegisterResponse> ResendCodeAsync(ResendCodeRequest request, CancellationToken ct = default)
    {
        var phone = NormalizePhoneOrThrow(request.PhoneNumber);
        var purpose = request.Purpose == CodePurpose.ResetPassword ? VerificationPurpose.ResetPassword : VerificationPurpose.VerifyPhone;
        var user = await uow.Users.GetByPhoneAsync(phone, ct);
        // Same answer whether the account exists or not (no user enumeration)
        if (user is null || (purpose == VerificationPurpose.VerifyPhone && user.PhoneConfirmed))
            return new RegisterResponse(Guid.Empty, PhoneNumberHelper.Mask(phone), purpose == VerificationPurpose.VerifyPhone, null);

        var code = await CreateCodeAsync(phone, purpose, user.Id, ct);
        await uow.SaveChangesAsync(ct);
        await SendCodeSmsAsync(phone, code, purpose, ct);
        return new RegisterResponse(user.Id, PhoneNumberHelper.Mask(phone), true, _sec.ExposeDevCodes ? code : null);
    }

    // ------------------------------------------------------------ Login
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await FindByIdentifierAsync(request.Identifier, ct);
        if (user is null)
        {
            hasher.Verify(request.Password, DummyHash); // same timing as a real check
            logger.LogSecurity("Login failed: unknown identifier from {Ip}", currentUser.IpAddress);
            throw new UnauthorizedException(InvalidCredentials, "invalid_credentials");
        }

        var now = clock.UtcNow;
        if (user.LockoutEndUtc is { } end && end > now)
        {
            logger.LogSecurity("Login refused: account {UserId} locked until {End}", user.Id, end);
            throw new AccountLockedException(end);
        }

        if (!hasher.Verify(request.Password, user.PasswordHash))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= _sec.MaxFailedLoginAttempts)
            {
                user.LockoutEndUtc = now.AddMinutes(_sec.LockoutMinutes);
                user.AccessFailedCount = 0;
                await audit.AddAsync("account_locked", user.Id, null, ct);
                logger.LogSecurity("Account {UserId} locked after too many failed logins from {Ip}", user.Id, currentUser.IpAddress);
            }
            else await audit.AddAsync("login_failed", user.Id, null, ct);
            await uow.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials, "invalid_credentials");
        }

        if (!user.IsActive) throw new ForbiddenException("Ce compte est désactivé.", "account_disabled");

        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        if (hasher.NeedsRehash(user.PasswordHash)) user.PasswordHash = hasher.Hash(request.Password);

        if (!user.PhoneConfirmed)
        {
            var code = await CreateCodeAsync(user.PhoneNumber, VerificationPurpose.VerifyPhone, user.Id, ct, enforceCooldown: false);
            await uow.SaveChangesAsync(ct);
            await SendCodeSmsAsync(user.PhoneNumber, code, VerificationPurpose.VerifyPhone, ct);
            return new LoginResponse { RequiresPhoneVerification = true, PhoneNumberMasked = PhoneNumberHelper.Mask(user.PhoneNumber) };
        }

        if (user.TwoFactorEnabled)
        {
            var challenge = new VerificationCode
            {
                UserId = user.Id, Target = user.Id.ToString(), Purpose = VerificationPurpose.MfaChallenge,
                ExpiresAt = now.AddMinutes(_sec.MfaChallengeMinutes)
            };
            await uow.VerificationCodes.AddAsync(challenge, ct);
            await uow.SaveChangesAsync(ct);
            return new LoginResponse { RequiresTwoFactor = true, ChallengeId = challenge.Id };
        }

        await audit.AddAsync("login", user.Id, request.DeviceName, ct);
        return new LoginResponse { Auth = await IssueTokensAsync(user, request.DeviceName, null, ct) };
    }

    public async Task<AuthResponse> LoginTwoFactorAsync(TwoFactorLoginRequest request, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var challenge = await uow.VerificationCodes.FirstOrDefaultAsync(
            c => c.Id == request.ChallengeId && c.Purpose == VerificationPurpose.MfaChallenge, ct);
        if (challenge is null || challenge.ConsumedAt is not null || challenge.ExpiresAt < now
            || challenge.Attempts >= _sec.VerificationCodeMaxAttempts || challenge.UserId is null)
            throw new UnauthorizedException("Session de connexion expirée. Reconnectez-vous.", "mfa_expired");

        var user = await uow.Users.GetByIdAsync(challenge.UserId.Value, ct)
                   ?? throw new UnauthorizedException("Session de connexion expirée.", "mfa_expired");
        if (!user.TwoFactorEnabled || user.TwoFactorSecretProtected is null)
            throw new UnauthorizedException("Double authentification non activée.", "mfa_not_enabled");

        var secret = protector.Unprotect(user.TwoFactorSecretProtected);
        if (!Totp.Verify(secret, request.Code, now))
        {
            challenge.Attempts++;
            await audit.AddAsync("mfa_failed", user.Id, null, ct);
            await uow.SaveChangesAsync(ct);
            throw new UnauthorizedException("Code de vérification incorrect.", "invalid_code");
        }

        challenge.ConsumedAt = now;
        await audit.AddAsync("login_mfa", user.Id, request.DeviceName, ct);
        return await IssueTokensAsync(user, request.DeviceName, null, ct);
    }

    // ------------------------------------------------------------ Tokens
    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var hash = SecureRandom.Sha256(request.RefreshToken);
        var stored = await uow.RefreshTokens.GetByHashAsync(hash, ct);
        if (stored is null) throw new UnauthorizedException("Session expirée. Reconnectez-vous.", "invalid_refresh_token");

        var now = clock.UtcNow;
        if (stored.RevokedAt is not null)
        {
            // A revoked token was presented again: it was probably stolen. Kill the whole session family.
            await uow.RefreshTokens.RevokeFamilyAsync(stored.FamilyId, "reuse_detected", ct);
            await audit.AddAsync("refresh_token_reuse", stored.UserId, stored.FamilyId.ToString(), ct);
            await uow.SaveChangesAsync(ct);
            logger.LogSecurity("Refresh token reuse detected for user {UserId}, family {Family}", stored.UserId, stored.FamilyId);
            throw new UnauthorizedException("Session expirée. Reconnectez-vous.", "invalid_refresh_token");
        }

        if (stored.ExpiresAt <= now || !stored.User.IsActive)
            throw new UnauthorizedException("Session expirée. Reconnectez-vous.", "invalid_refresh_token");

        var response = await IssueTokensAsync(stored.User, request.DeviceName ?? stored.DeviceName, stored.FamilyId, ct, save: false);
        stored.RevokedAt = now;
        stored.RevokedReason = "rotated";
        stored.ReplacedByTokenHash = SecureRandom.Sha256(response.RefreshToken);
        await uow.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        if (request.AllDevices)
        {
            await uow.RefreshTokens.RevokeAllForUserAsync(userId, "logout_all", ct);
            var user = await uow.Users.GetByIdAsync(userId, ct);
            if (user is not null) user.SecurityStamp = Guid.NewGuid().ToString("N");
            sessionCache.Invalidate(userId);
        }
        else if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var stored = await uow.RefreshTokens.GetByHashAsync(SecureRandom.Sha256(request.RefreshToken), ct);
            if (stored is not null && stored.UserId == userId)
                await uow.RefreshTokens.RevokeFamilyAsync(stored.FamilyId, "logout", ct);
        }
        await audit.AddAsync(request.AllDevices ? "logout_all" : "logout", userId, null, ct);
        await uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ Passwords
    public async Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await FindByIdentifierAsync(request.Identifier, ct);
        if (user is null || !user.IsActive) return null; // no user enumeration: the controller always answers 202
        var code = await CreateCodeAsync(user.PhoneNumber, VerificationPurpose.ResetPassword, user.Id, ct);
        await audit.AddAsync("password_reset_requested", user.Id, null, ct);
        await uow.SaveChangesAsync(ct);
        await SendCodeSmsAsync(user.PhoneNumber, code, VerificationPurpose.ResetPassword, ct);
        return _sec.ExposeDevCodes ? code : null;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var identifier = string.IsNullOrWhiteSpace(request.Identifier) ? request.PhoneNumber : request.Identifier;
        if (string.IsNullOrWhiteSpace(identifier))
            throw ValidationException.For(nameof(request.Identifier), "Indiquez votre email ou votre numéro de téléphone.");
        var errors = PasswordPolicy.Validate(request.NewPassword);
        if (errors.Count > 0) throw new ValidationException(new Dictionary<string, string[]> { [nameof(request.NewPassword)] = errors.ToArray() });

        // The code was sent to (and stored for) the account's phone, whatever identifier was used
        var user = await FindByIdentifierAsync(identifier, ct);
        if (user is null || !user.IsActive) throw new BadRequestException("Code invalide ou expiré.", "invalid_code");
        await ConsumeCodeAsync(user.PhoneNumber, VerificationPurpose.ResetPassword, request.Code, ct);

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.PhoneConfirmed = true;
        user.AccessFailedCount = 0;
        user.LockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await uow.RefreshTokens.RevokeAllForUserAsync(user.Id, "password_reset", ct);
        await audit.AddAsync("password_reset", user.Id, null, ct);
        await uow.SaveChangesAsync(ct);
        sessionCache.Invalidate(user.Id);
    }

    public async Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await uow.Users.GetByIdAsync(currentUser.RequireUserId(), ct) ?? throw new UnauthorizedException();
        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw ValidationException.For(nameof(request.CurrentPassword), "Mot de passe actuel incorrect.");
        var errors = PasswordPolicy.Validate(request.NewPassword);
        if (errors.Count > 0) throw new ValidationException(new Dictionary<string, string[]> { [nameof(request.NewPassword)] = errors.ToArray() });

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await uow.RefreshTokens.RevokeAllForUserAsync(user.Id, "password_changed", ct);
        await audit.AddAsync("password_changed", user.Id, null, ct);
        sessionCache.Invalidate(user.Id);
        return await IssueTokensAsync(user, null, null, ct);
    }

    // ------------------------------------------------------------ Two-factor (TOTP)
    public async Task<TwoFactorSetupResponse> SetupTwoFactorAsync(CancellationToken ct = default)
    {
        var user = await uow.Users.GetByIdAsync(currentUser.RequireUserId(), ct) ?? throw new UnauthorizedException();
        if (user.TwoFactorEnabled) throw new ConflictException("La double authentification est déjà activée.");
        var secret = Totp.GenerateSecret();
        user.TwoFactorSecretProtected = protector.Protect(secret);
        await uow.SaveChangesAsync(ct);
        return new TwoFactorSetupResponse(secret, Totp.BuildOtpAuthUri("TuniPlan", user.Email ?? user.PhoneNumber, secret));
    }

    public async Task EnableTwoFactorAsync(TwoFactorCodeRequest request, CancellationToken ct = default)
    {
        var user = await uow.Users.GetByIdAsync(currentUser.RequireUserId(), ct) ?? throw new UnauthorizedException();
        if (user.TwoFactorSecretProtected is null) throw new BadRequestException("Lancez d'abord la configuration de la double authentification.");
        if (!Totp.Verify(protector.Unprotect(user.TwoFactorSecretProtected), request.Code, clock.UtcNow))
            throw ValidationException.For(nameof(request.Code), "Code incorrect.");
        user.TwoFactorEnabled = true;
        await audit.AddAsync("mfa_enabled", user.Id, null, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task DisableTwoFactorAsync(TwoFactorCodeRequest request, CancellationToken ct = default)
    {
        var user = await uow.Users.GetByIdAsync(currentUser.RequireUserId(), ct) ?? throw new UnauthorizedException();
        if (!user.TwoFactorEnabled || user.TwoFactorSecretProtected is null) return;
        if (!Totp.Verify(protector.Unprotect(user.TwoFactorSecretProtected), request.Code, clock.UtcNow))
            throw ValidationException.For(nameof(request.Code), "Code incorrect.");
        user.TwoFactorEnabled = false;
        user.TwoFactorSecretProtected = null;
        await audit.AddAsync("mfa_disabled", user.Id, null, ct);
        await uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ Sessions
    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var active = await uow.RefreshTokens.QueryNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return active.Select(t => new SessionDto(t.FamilyId, t.DeviceName, t.CreatedByIp, t.CreatedAt, t.ExpiresAt)).ToList();
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        if (!await uow.RefreshTokens.AnyAsync(t => t.FamilyId == sessionId && t.UserId == userId, ct))
            throw new NotFoundException("Session introuvable.");
        await uow.RefreshTokens.RevokeFamilyAsync(sessionId, "revoked_by_user", ct);
        await uow.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ Helpers
    private static readonly string DummyHash = new Pbkdf2PasswordHasher().Hash("dummy-password-for-timing");

    private async Task<User?> FindByIdentifierAsync(string identifier, CancellationToken ct)
    {
        var value = identifier.Trim();
        if (PhoneNumberHelper.LooksLikeEmail(value)) return await uow.Users.GetByEmailAsync(value.ToLowerInvariant(), ct);
        var phone = PhoneNumberHelper.NormalizeTunisian(value);
        return phone is null ? null : await uow.Users.GetByPhoneAsync(phone, ct);
    }

    private static string NormalizePhoneOrThrow(string input) =>
        PhoneNumberHelper.NormalizeTunisian(input)
        ?? throw ValidationException.For("PhoneNumber", "Numéro de téléphone tunisien invalide (8 chiffres).");

    private async Task<string> CreateCodeAsync(string target, VerificationPurpose purpose, Guid? userId, CancellationToken ct, bool enforceCooldown = true)
    {
        var now = clock.UtcNow;
        var recent = await uow.VerificationCodes.QueryNoTracking()
            .Where(c => c.Target == target && c.Purpose == purpose && c.CreatedAt > now.AddHours(-1))
            .OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

        if (recent.Count >= _sec.VerificationCodesPerHour)
            throw new TooManyRequestsException("Trop de codes demandés. Réessayez dans une heure.");
        if (enforceCooldown && recent.Count > 0 && recent[0].CreatedAt > now.AddSeconds(-_sec.VerificationCodeResendSeconds))
            throw new TooManyRequestsException($"Patientez {_sec.VerificationCodeResendSeconds} secondes avant de demander un nouveau code.");

        // Invalidate previous codes for the same purpose
        var previous = await uow.VerificationCodes.Query()
            .Where(c => c.Target == target && c.Purpose == purpose && c.ConsumedAt == null).ToListAsync(ct);
        foreach (var p in previous) p.ConsumedAt = now;

        var code = SecureRandom.NumericCode(6);
        await uow.VerificationCodes.AddAsync(new VerificationCode
        {
            UserId = userId, Target = target, Purpose = purpose,
            CodeHash = SecureRandom.Sha256($"{target}:{purpose}:{code}"),
            ExpiresAt = now.AddMinutes(_sec.VerificationCodeMinutes)
        }, ct);
        return code;
    }

    private async Task ConsumeCodeAsync(string target, VerificationPurpose purpose, string code, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var entry = await uow.VerificationCodes.Query()
            .Where(c => c.Target == target && c.Purpose == purpose && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

        if (entry is null || entry.ExpiresAt < now || entry.Attempts >= _sec.VerificationCodeMaxAttempts || entry.CodeHash is null)
            throw new BadRequestException("Code invalide ou expiré. Demandez un nouveau code.", "invalid_code");

        if (!SecureRandom.FixedTimeEqualsHex(entry.CodeHash, SecureRandom.Sha256($"{target}:{purpose}:{code}")))
        {
            entry.Attempts++;
            await uow.SaveChangesAsync(ct);
            logger.LogSecurity("Wrong verification code for {Target} ({Purpose})", PhoneNumberHelper.Mask(target), purpose);
            throw new BadRequestException("Code incorrect.", "invalid_code");
        }
        entry.ConsumedAt = now;
    }

    private async Task SendCodeSmsAsync(string phone, string code, VerificationPurpose purpose, CancellationToken ct)
    {
        var text = purpose == VerificationPurpose.ResetPassword
            ? $"TuniPlan : votre code de réinitialisation est {code}. Il expire dans {_sec.VerificationCodeMinutes} min. Ne le partagez jamais."
            : $"TuniPlan : votre code de vérification est {code}. Il expire dans {_sec.VerificationCodeMinutes} min.";
        var ok = await sms.SendAsync(new OutgoingMessage(phone, text), ct);
        if (!ok) logger.LogWarn("Verification SMS could not be sent to {Phone}", PhoneNumberHelper.Mask(phone));
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? deviceName, Guid? familyId, CancellationToken ct, bool save = true)
    {
        var now = clock.UtcNow;
        var (access, accessExpires) = tokens.CreateAccessToken(user);
        var refresh = SecureRandom.Token();
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);
        await uow.RefreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = SecureRandom.Sha256(refresh),
            FamilyId = familyId ?? Guid.NewGuid(),
            ExpiresAt = refreshExpires,
            CreatedByIp = currentUser.IpAddress,
            DeviceName = deviceName?.Length > 200 ? deviceName[..200] : deviceName
        }, ct);
        user.LastLoginAt = now;
        if (save) await uow.SaveChangesAsync(ct);

        var memberships = await uow.OrganizationMembers.QueryNoTracking()
            .Include(m => m.Organization)
            .Where(m => m.UserId == user.Id).ToListAsync(ct);
        return new AuthResponse(access, accessExpires, refresh, refreshExpires, user.ToProfile(memberships));
    }
}
