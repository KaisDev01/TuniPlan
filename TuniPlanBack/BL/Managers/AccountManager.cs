using BL.Interfaces;
using BL.Mapping;
using Common.Exceptions;
using Common.Helpers;
using Common.Security;
using DAO.Interfaces;
using DTOs.Account;
using DTOs.Organizations;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using OperationStorage;

namespace BL.Managers;

public interface IAccountManager
{
    Task<UserProfileDto> GetProfileAsync(CancellationToken ct = default);
    Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default);
    Task<UserProfileDto> UploadAvatarAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<UserProfileDto> EnableBusinessModeAsync(CancellationToken ct = default);
    Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken ct = default);
    Task<NotificationSettingsDto> UpdateNotificationSettingsAsync(NotificationSettingsDto request, CancellationToken ct = default);

    Task<IReadOnlyList<FamilyMemberDto>> GetFamilyAsync(CancellationToken ct = default);
    Task<FamilyMemberDto> AddFamilyMemberAsync(UpsertFamilyMemberRequest request, CancellationToken ct = default);
    Task<FamilyMemberDto> UpdateFamilyMemberAsync(Guid id, UpsertFamilyMemberRequest request, CancellationToken ct = default);
    Task DeleteFamilyMemberAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<OrganizationCardDto>> GetFavoritesAsync(CancellationToken ct = default);
    Task AddFavoriteAsync(Guid organizationId, CancellationToken ct = default);
    Task RemoveFavoriteAsync(Guid organizationId, CancellationToken ct = default);

    Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default);
    Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default);
    Task RemoveDeviceAsync(Guid id, CancellationToken ct = default);
}

public sealed class AccountManager(
    IUnitOfWork uow, ICurrentUser currentUser, IPasswordHasher hasher, IFileStorage storage,
    IUserSessionCache sessionCache, IAuditManager audit) : IAccountManager
{
    private async Task<User> CurrentAsync(CancellationToken ct) =>
        await uow.Users.GetByIdAsync(currentUser.RequireUserId(), ct) ?? throw new UnauthorizedException();

    private async Task<UserProfileDto> ProfileAsync(User user, CancellationToken ct)
    {
        var memberships = await uow.OrganizationMembers.QueryNoTracking().Include(m => m.Organization)
            .Where(m => m.UserId == user.Id).ToListAsync(ct);
        return user.ToProfile(memberships);
    }

    public async Task<UserProfileDto> GetProfileAsync(CancellationToken ct = default) => await ProfileAsync(await CurrentAsync(ct), ct);

    public async Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await CurrentAsync(ct);
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        if (email != user.Email)
        {
            if (email is not null && await uow.Users.AnyAsync(u => u.Email == email && u.Id != user.Id, ct))
                throw new ConflictException("Cet email est déjà utilisé.", "email_taken");
            user.Email = email;
            user.EmailConfirmed = false;
        }
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PreferredLanguage = request.PreferredLanguage;
        await uow.SaveChangesAsync(ct);
        return await ProfileAsync(user, ct);
    }

    public async Task<UserProfileDto> UploadAvatarAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var user = await CurrentAsync(ct);
        StoredFile stored;
        try { stored = await storage.SaveImageAsync(content, fileName, contentType, "avatars", ct); }
        catch (InvalidFileException ex) { throw new BadRequestException(ex.Message, "invalid_file"); }
        if (user.AvatarUrl is not null) await storage.DeleteAsync(user.AvatarUrl, ct);
        user.AvatarUrl = stored.Url;
        await uow.SaveChangesAsync(ct);
        return await ProfileAsync(user, ct);
    }

    public async Task<UserProfileDto> EnableBusinessModeAsync(CancellationToken ct = default)
    {
        var user = await CurrentAsync(ct);
        if (!user.Roles.HasFlag(AccountRoles.Business))
        {
            user.Roles |= AccountRoles.Business;
            user.SecurityStamp = Guid.NewGuid().ToString("N"); // new token needed to carry the new role
            await audit.AddAsync("business_mode_enabled", user.Id, null, ct);
            await uow.SaveChangesAsync(ct);
            sessionCache.Invalidate(user.Id);
        }
        return await ProfileAsync(user, ct);
    }

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken ct = default) =>
        (await CurrentAsync(ct)).NotificationSettings.ToDto();

    public async Task<NotificationSettingsDto> UpdateNotificationSettingsAsync(NotificationSettingsDto request, CancellationToken ct = default)
    {
        var user = await CurrentAsync(ct);
        var s = user.NotificationSettings;
        s.PushEnabled = request.PushEnabled;
        s.SmsEnabled = request.SmsEnabled;
        s.WhatsAppEnabled = request.WhatsAppEnabled;
        s.EmailEnabled = request.EmailEnabled;
        s.Reminder24h = request.Reminder24h;
        s.Reminder2h = request.Reminder2h;
        await uow.SaveChangesAsync(ct);
        return s.ToDto();
    }

    // ---------------- Family
    public async Task<IReadOnlyList<FamilyMemberDto>> GetFamilyAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var list = await uow.FamilyMembers.ListAsync(f => f.UserId == userId, ct);
        return list.OrderBy(f => f.FirstName).Select(f => f.ToDto()).ToList();
    }

    public async Task<FamilyMemberDto> AddFamilyMemberAsync(UpsertFamilyMemberRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        if (await uow.FamilyMembers.CountAsync(f => f.UserId == userId, ct) >= 10)
            throw new BusinessRuleException("Vous pouvez ajouter au maximum 10 proches.");
        var member = new FamilyMember { UserId = userId };
        Apply(member, request);
        await uow.FamilyMembers.AddAsync(member, ct);
        await uow.SaveChangesAsync(ct);
        return member.ToDto();
    }

    public async Task<FamilyMemberDto> UpdateFamilyMemberAsync(Guid id, UpsertFamilyMemberRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var member = await uow.FamilyMembers.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, ct)
                     ?? throw new NotFoundException("Proche introuvable.");
        Apply(member, request);
        await uow.SaveChangesAsync(ct);
        return member.ToDto();
    }

    public async Task DeleteFamilyMemberAsync(Guid id, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var member = await uow.FamilyMembers.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, ct)
                     ?? throw new NotFoundException("Proche introuvable.");
        if (await uow.Appointments.AnyAsync(a => a.FamilyMemberId == id, ct))
            throw new BusinessRuleException("Ce proche a des rendez-vous : il ne peut pas être supprimé.");
        uow.FamilyMembers.Remove(member);
        await uow.SaveChangesAsync(ct);
    }

    private static void Apply(FamilyMember m, UpsertFamilyMemberRequest r)
    {
        m.FirstName = r.FirstName.Trim();
        m.LastName = r.LastName.Trim();
        m.Relation = r.Relation.Trim();
        m.PhoneNumber = string.IsNullOrWhiteSpace(r.PhoneNumber) ? null
            : PhoneNumberHelper.NormalizeTunisian(r.PhoneNumber) ?? throw ValidationException.For("PhoneNumber", "Numéro invalide.");
    }

    // ---------------- Favorites
    public async Task<IReadOnlyList<OrganizationCardDto>> GetFavoritesAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var orgs = await uow.Favorites.QueryNoTracking().Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Organization).Where(o => o.IsPublished)
            .Select(o => new OrganizationCardDto
            {
                Id = o.Id, Name = o.Name, Slug = o.Slug, Category = o.Category, Subcategory = o.Subcategory,
                City = o.City, Governorate = o.Governorate, CoverUrl = o.CoverUrl, LogoUrl = o.LogoUrl,
                RatingAverage = o.RatingAverage, ReviewCount = o.ReviewCount, IsFavorite = true,
                IsVerified = o.VerificationStatus == VerificationStatus.Verified, BookingType = o.BookingType,
                Latitude = o.Latitude, Longitude = o.Longitude,
                PriceFrom = o.Services.Where(s => s.IsActive && !s.IsDeleted).Min(s => (decimal?)s.Price)
            }).ToListAsync(ct);
        return orgs;
    }

    public async Task AddFavoriteAsync(Guid organizationId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        if (!await uow.Organizations.AnyAsync(o => o.Id == organizationId && o.IsPublished, ct)) throw new NotFoundException("Entreprise introuvable.");
        if (await uow.Favorites.AnyAsync(f => f.UserId == userId && f.OrganizationId == organizationId, ct)) return;
        await uow.Favorites.AddAsync(new Favorite { UserId = userId, OrganizationId = organizationId }, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task RemoveFavoriteAsync(Guid organizationId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var fav = await uow.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.OrganizationId == organizationId, ct);
        if (fav is null) return;
        uow.Favorites.Remove(fav);
        await uow.SaveChangesAsync(ct);
    }

    // ---------------- Delete account (soft delete + anonymization)
    public async Task DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default)
    {
        var user = await CurrentAsync(ct);
        if (!hasher.Verify(request.Password, user.PasswordHash))
            throw ValidationException.For(nameof(request.Password), "Mot de passe incorrect.");
        if (await uow.OrganizationMembers.AnyAsync(m => m.UserId == user.Id && m.Role == MemberRole.Owner, ct))
            throw new BusinessRuleException("Vous êtes propriétaire d'une entreprise : supprimez-la ou transférez-la d'abord.");

        var now = DateTime.UtcNow;
        var future = await uow.Appointments.Query()
            .Where(a => a.ClientUserId == user.Id && a.StartUtc > now
                        && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.CounterProposed))
            .ToListAsync(ct);
        foreach (var a in future) { a.Status = AppointmentStatus.CancelledByClient; a.CancelledAt = now; a.CancelReason = "Compte supprimé"; }

        await uow.RefreshTokens.RevokeAllForUserAsync(user.Id, "account_deleted", ct);
        uow.UserDevices.RemoveRange(await uow.UserDevices.Query().Where(d => d.UserId == user.Id).ToListAsync(ct)); // no more pushes
        user.IsActive = false;
        user.FirstName = "Utilisateur";
        user.LastName = "supprimé";
        user.Email = null;
        user.PhoneNumber = $"deleted-{user.Id:N}"[..20];
        user.PasswordHash = hasher.Hash(SecureRandom.Token(32));
        user.TwoFactorSecretProtected = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await audit.AddAsync("account_deleted", user.Id, null, ct);
        uow.Users.Remove(user); // soft delete (interceptor)
        await uow.SaveChangesAsync(ct);
        sessionCache.Invalidate(user.Id);
    }

    // ------------------------------------------------------------ Push devices
    public async Task<DeviceDto> RegisterDeviceAsync(RegisterDeviceRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var token = request.Token.Trim();
        // One token = one phone: if another account used this phone before, the token moves to the current account
        var device = await uow.UserDevices.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (device is null)
        {
            device = new UserDevice { UserId = userId, Token = token };
            await uow.UserDevices.AddAsync(device, ct);
        }
        device.UserId = userId;
        device.Platform = request.Platform;
        device.DeviceName = request.DeviceName?.Trim();
        device.LastSeenAt = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        return new DeviceDto(device.Id, device.Platform, device.DeviceName, device.CreatedAt, device.LastSeenAt);
    }

    public async Task RemoveDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var device = await uow.UserDevices.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct)
                     ?? throw new NotFoundException("Appareil introuvable.");
        uow.UserDevices.Remove(device);
        await uow.SaveChangesAsync(ct);
    }
}
