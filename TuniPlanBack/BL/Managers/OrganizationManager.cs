using BL.Availability;
using BL.Interfaces;
using BL.Mapping;
using BL.Options;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Catalog;
using DTOs.Organizations;
using DTOs.Reviews;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OperationStorage;

namespace BL.Managers;

public interface IOrganizationManager
{
    // Public (clients)
    Task<PagedResult<OrganizationCardDto>> SearchAsync(OrganizationSearchQuery query, CancellationToken ct = default);
    Task<OrganizationDetailsDto> GetPublicAsync(Guid id, CancellationToken ct = default);
    Task<OrganizationDetailsDto> GetPublicBySlugAsync(string slug, CancellationToken ct = default);

    // Business owner
    Task<IReadOnlyList<OrganizationCardDto>> GetMineAsync(CancellationToken ct = default);
    Task<MyOrganizationDto> CreateAsync(UpsertOrganizationRequest request, CancellationToken ct = default);
    Task<MyOrganizationDto> GetMyOrganizationAsync(Guid id, CancellationToken ct = default);
    Task<MyOrganizationDto> UpdateAsync(Guid id, UpsertOrganizationRequest request, CancellationToken ct = default);
    Task<MyOrganizationDto> UpdateOpeningHoursAsync(Guid id, IReadOnlyList<OpeningHourDto> hours, CancellationToken ct = default);
    Task<MyOrganizationDto> UpdateRulesAsync(Guid id, BookingRulesDto rules, CancellationToken ct = default);
    Task<MyOrganizationDto> SubmitVerificationAsync(Guid id, VerificationRequest request, CancellationToken ct = default);
    Task<MyOrganizationDto> SetPublishedAsync(Guid id, bool published, CancellationToken ct = default);
    Task<MyOrganizationDto> UploadImageAsync(Guid id, string kind, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<MyOrganizationDto> DeletePhotoAsync(Guid id, string url, CancellationToken ct = default);
    Task<ProfileCompletionDto> GetCompletionAsync(Guid id, CancellationToken ct = default);
    Task<PublicLinkDto> GetPublicLinkAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ClosedPeriodDto>> GetClosedPeriodsAsync(Guid id, DateTime? fromUtc, CancellationToken ct = default);
    Task<ClosedPeriodDto> AddClosedPeriodAsync(Guid id, CreateClosedPeriodRequest request, CancellationToken ct = default);
    Task DeleteClosedPeriodAsync(Guid id, Guid periodId, CancellationToken ct = default);

    // Admin
    Task<IReadOnlyList<MyOrganizationDto>> GetPendingVerificationsAsync(CancellationToken ct = default);
    Task SetVerificationStatusAsync(Guid id, bool approved, CancellationToken ct = default);
}

public sealed class OrganizationManager(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IOrganizationAccess access,
    IAvailabilityManager availability,
    IFileStorage storage,
    INotificationManager notifications,
    IClock clock,
    IOptions<AppOptions> appOptions) : IOrganizationManager
{
    private const int MaxSearchCandidates = 300;
    private const int MaxPhotos = 12;

    // =========================================================== Public search
    public async Task<PagedResult<OrganizationCardDto>> SearchAsync(OrganizationSearchQuery query, CancellationToken ct = default)
    {
        var q = uow.Organizations.SearchPublished();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            q = q.Where(o => o.Name.Contains(term) || (o.Subcategory != null && o.Subcategory.Contains(term))
                             || o.City.Contains(term) || (o.Description != null && o.Description.Contains(term))
                             || o.Services.Any(s => s.IsActive && s.Name.Contains(term)));
        }
        if (query.Category is { } cat) q = q.Where(o => o.Category == cat);
        if (!string.IsNullOrWhiteSpace(query.Governorate)) q = q.Where(o => o.Governorate == query.Governorate);
        if (!string.IsNullOrWhiteSpace(query.City)) q = q.Where(o => o.City.Contains(query.City));
        if (query.MinRating is { } minRating) q = q.Where(o => o.RatingAverage >= minRating);
        if (query.MaxPrice is { } maxPrice) q = q.Where(o => o.Services.Any(s => s.IsActive && s.Price <= maxPrice));

        var candidates = await q
            .Include(o => o.OpeningHours)
            .Include(o => o.Services.Where(s => s.IsActive)).ThenInclude(s => s.ServiceResources)
            .Include(o => o.Promotions.Where(p => p.IsActive))
            .OrderByDescending(o => o.RatingAverage).ThenByDescending(o => o.ReviewCount)
            .Take(MaxSearchCandidates)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var favoriteIds = await FavoriteIdsAsync(ct);
        var cards = candidates.Select(o => ToCard(o, now, favoriteIds, query.Lat, query.Lng)).ToList();

        if (query.OpenNow) cards = cards.Where(c => c.OpenNow).ToList();

        if (query.Date is { } date)
        {
            var filtered = new List<OrganizationCardDto>();
            foreach (var card in cards)
            {
                var org = candidates.First(o => o.Id == card.Id);
                var service = org.Services.OrderBy(s => s.SortOrder).FirstOrDefault();
                if (service is null) continue;
                var day = await availability.GetAvailabilityAsync(org.Id, new AvailabilityQuery { ServiceId = service.Id, From = date, Days = 1 }, ct);
                if (day.Any(d => d.Slots.Count > 0)) filtered.Add(card);
            }
            cards = filtered;
        }

        cards = query.Sort switch
        {
            "rating" => cards.OrderByDescending(c => c.RatingAverage).ThenByDescending(c => c.ReviewCount).ToList(),
            "price" => cards.OrderBy(c => c.PriceFrom ?? decimal.MaxValue).ToList(),
            "distance" => cards.OrderBy(c => c.DistanceKm ?? double.MaxValue).ToList(),
            _ => cards.OrderByDescending(c => c.IsVerified).ThenByDescending(c => c.RatingAverage * Math.Log10(c.ReviewCount + 10)).ToList()
        };

        var total = cards.Count;
        var page = cards.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();

        // Next free slot only for the returned page (costly)
        for (var i = 0; i < page.Count; i++)
        {
            var org = candidates.First(o => o.Id == page[i].Id);
            var service = org.Services.OrderBy(s => s.SortOrder).FirstOrDefault();
            if (service is null) continue;
            var next = await availability.GetNextSlotsAsync(org, service, 1, 7, null, ct);
            if (next.Count > 0) page[i] = page[i] with { NextSlotUtc = next[0].StartUtc };
        }
        return new PagedResult<OrganizationCardDto>(page, query.Page, query.PageSize, total);
    }

    public async Task<OrganizationDetailsDto> GetPublicAsync(Guid id, CancellationToken ct = default)
    {
        var org = await uow.Organizations.GetDetailsAsync(id, ct);
        if (org is null || (!org.IsPublished && !await CanSeeUnpublishedAsync(org.Id, ct))) throw new NotFoundException("Entreprise introuvable.");
        return await BuildDetailsAsync(org, ct);
    }

    public async Task<OrganizationDetailsDto> GetPublicBySlugAsync(string slug, CancellationToken ct = default)
    {
        var org = await uow.Organizations.GetDetailsBySlugAsync(slug.ToLowerInvariant(), ct);
        if (org is null || (!org.IsPublished && !await CanSeeUnpublishedAsync(org.Id, ct))) throw new NotFoundException("Entreprise introuvable.");
        return await BuildDetailsAsync(org, ct);
    }

    private async Task<bool> CanSeeUnpublishedAsync(Guid orgId, CancellationToken ct) =>
        currentUser.UserId is { } uid && (currentUser.IsInRole(Roles.Admin) || await uow.Organizations.IsMemberAsync(orgId, uid, null, ct));

    private async Task<OrganizationDetailsDto> BuildDetailsAsync(Organization org, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        var localNow = TimeZoneHelper.ToLocal(now, tz);
        var favorites = await FavoriteIdsAsync(ct);

        var reviews = uow.Reviews.QueryNoTracking().Where(r => r.OrganizationId == org.Id && !r.IsHidden);
        var latest = await reviews.Include(r => r.ClientUser).Include(r => r.Appointment).ThenInclude(a => a.Service)
            .OrderByDescending(r => r.CreatedAt).Take(3).ToListAsync(ct);
        var summary = await ReviewManager.BuildSummaryAsync(reviews, ct);

        var services = org.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToList();
        var firstService = services.FirstOrDefault();
        IReadOnlyList<SlotDto> nextSlots = firstService is null ? [] : await availability.GetNextSlotsAsync(org, firstService, 6, 14, null, ct);

        return new OrganizationDetailsDto
        {
            Id = org.Id, Name = org.Name, Slug = org.Slug, Category = org.Category, Subcategory = org.Subcategory,
            Description = org.Description, Governorate = org.Governorate, City = org.City, Address = org.Address,
            Latitude = org.Latitude, Longitude = org.Longitude, Phone = org.Phone, WhatsApp = org.WhatsApp, Email = org.Email,
            LogoUrl = org.LogoUrl, CoverUrl = org.CoverUrl,
            Photos = org.Photos.OrderBy(p => p.SortOrder).Select(p => p.Url).ToList(),
            OpeningHours = org.OpeningHours.OrderBy(h => ((int)h.DayOfWeek + 6) % 7).Select(h => h.ToDto()).ToList(),
            OpenNow = SlotCalculator.IsOpenAt(now, tz, org.OpeningHours),
            Team = org.Resources.OrderBy(r => r.Name).Select(r => r.ToDto()).ToList(),
            Services = services.Select(s =>
            {
                var discount = PromotionRules.BestDiscount(org.Promotions, s.Id, localNow);
                return s.ToDto(discount > 0 ? (decimal?)decimal.Round(s.Price * (100 - discount) / 100m, 3) : null);
            }).ToList(),
            Promotions = org.Promotions.Where(p => p.ValidTo >= DateOnly.FromDateTime(localNow)).Select(p => p.ToDto()).ToList(),
            PriceFrom = services.Count == 0 ? null : services.Min(s => s.Price),
            BookingType = org.BookingType,
            ManualValidationRequired = org.ManualValidationRequired,
            CancellationDeadlineHours = org.CancellationDeadlineHours,
            DepositMode = org.DepositMode,
            DepositValue = org.DepositValue,
            IsVerified = org.VerificationStatus == VerificationStatus.Verified,
            IsFavorite = favorites.Contains(org.Id),
            ReviewSummary = summary,
            LatestReviews = latest.Select(r => r.ToDto()).ToList(),
            NextSlots = nextSlots
        };
    }

    private OrganizationCardDto ToCard(Organization o, DateTime now, HashSet<Guid> favorites, double? lat, double? lng)
    {
        var tz = TimeZoneHelper.Find(o.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(now, tz));
        return new OrganizationCardDto
        {
            Id = o.Id, Name = o.Name, Slug = o.Slug, Category = o.Category, Subcategory = o.Subcategory,
            City = o.City, Governorate = o.Governorate, CoverUrl = o.CoverUrl, LogoUrl = o.LogoUrl,
            RatingAverage = o.RatingAverage, ReviewCount = o.ReviewCount,
            PriceFrom = o.Services.Count == 0 ? null : o.Services.Min(s => s.Price),
            OpenNow = SlotCalculator.IsOpenAt(now, tz, o.OpeningHours),
            IsFavorite = favorites.Contains(o.Id),
            HasPromotion = o.Promotions.Any(p => p.ValidFrom <= today && p.ValidTo >= today),
            IsVerified = o.VerificationStatus == VerificationStatus.Verified,
            BookingType = o.BookingType,
            Latitude = o.Latitude, Longitude = o.Longitude,
            DistanceKm = lat is not null && lng is not null && o.Latitude is not null && o.Longitude is not null
                ? Math.Round(Haversine(lat.Value, lng.Value, o.Latitude.Value, o.Longitude.Value), 1) : null
        };
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private async Task<HashSet<Guid>> FavoriteIdsAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } uid) return [];
        var ids = await uow.Favorites.QueryNoTracking().Where(f => f.UserId == uid).Select(f => f.OrganizationId).ToListAsync(ct);
        return ids.ToHashSet();
    }

    // =========================================================== Business owner
    public async Task<IReadOnlyList<OrganizationCardDto>> GetMineAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var orgs = await uow.Organizations.QueryNoTracking()
            .Where(o => o.Members.Any(m => m.UserId == userId))
            .Include(o => o.OpeningHours).Include(o => o.Services.Where(s => s.IsActive)).Include(o => o.Promotions)
            .OrderBy(o => o.Name).ToListAsync(ct);
        var now = clock.UtcNow;
        return orgs.Select(o => ToCard(o, now, [], null, null)).ToList();
    }

    public async Task<MyOrganizationDto> CreateAsync(UpsertOrganizationRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var user = await uow.Users.GetByIdAsync(userId, ct) ?? throw new UnauthorizedException();
        if (!user.Roles.HasFlag(AccountRoles.Business))
            throw new ForbiddenException("Activez d'abord le mode professionnel sur votre compte.", "business_mode_required");
        if (await uow.OrganizationMembers.CountAsync(m => m.UserId == userId && m.Role == MemberRole.Owner, ct) >= 5)
            throw new BusinessRuleException("Maximum 5 établissements par compte.");

        var org = new Organization { Slug = await UniqueSlugAsync(request.Name, ct) };
        await ApplyAsync(org, request, ct);
        org.Phone ??= user.PhoneNumber;
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            org.OpeningHours.Add(new OpeningHour
            {
                DayOfWeek = day, IsClosed = day == DayOfWeek.Sunday,
                OpenTime = new TimeOnly(9, 0), CloseTime = day == DayOfWeek.Saturday ? new TimeOnly(13, 0) : new TimeOnly(18, 0)
            });
        }
        org.Members.Add(new OrganizationMember { UserId = userId, Role = MemberRole.Owner });
        await uow.Organizations.AddAsync(org, ct);
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(org.Id, ct);
    }

    public async Task<MyOrganizationDto> GetMyOrganizationAsync(Guid id, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ct: ct);
        var org = await uow.Organizations.GetDetailsAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        return new MyOrganizationDto
        {
            Details = await BuildDetailsAsync(org, ct),
            Rules = org.ToRules(),
            VerificationStatus = org.VerificationStatus,
            TaxId = org.TaxId,
            RneNumber = org.RneNumber,
            IsPublished = org.IsPublished,
            Completion = await ComputeCompletionAsync(org, ct),
            PublicUrl = PublicUrl(org)
        };
    }

    public async Task<MyOrganizationDto> UpdateAsync(Guid id, UpsertOrganizationRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        var org = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        await ApplyAsync(org, request, ct);
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    private Task ApplyAsync(Organization org, UpsertOrganizationRequest r, CancellationToken ct)
    {
        if (!TunisiaReference.IsGovernorate(r.Governorate))
            throw ValidationException.For(nameof(r.Governorate), "Gouvernorat inconnu.");
        org.Name = r.Name.Trim();
        org.Category = r.Category;
        org.Subcategory = r.Subcategory?.Trim();
        org.Description = r.Description?.Trim();
        org.Governorate = TunisiaReference.Governorates.First(g => string.Equals(g, r.Governorate.Trim(), StringComparison.OrdinalIgnoreCase));
        org.City = r.City.Trim();
        org.Address = r.Address.Trim();
        org.Latitude = r.Latitude;
        org.Longitude = r.Longitude;
        org.Phone = NormalizeOptionalPhone(r.Phone, nameof(r.Phone)) ?? org.Phone;
        org.WhatsApp = NormalizeOptionalPhone(r.WhatsApp, nameof(r.WhatsApp));
        org.Email = r.Email?.Trim().ToLowerInvariant();
        return Task.CompletedTask;
    }

    private static string? NormalizeOptionalPhone(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? null
            : PhoneNumberHelper.NormalizeTunisian(value) ?? throw ValidationException.For(field, "Numéro de téléphone invalide.");

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = SlugHelper.Slugify(name);
        var slug = baseSlug;
        for (var i = 2; await uow.Organizations.SlugExistsAsync(slug, ct); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }

    public async Task<MyOrganizationDto> UpdateOpeningHoursAsync(Guid id, IReadOnlyList<OpeningHourDto> hours, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        if (hours.Select(h => h.DayOfWeek).Distinct().Count() != hours.Count)
            throw ValidationException.For("hours", "Chaque jour ne doit apparaître qu'une fois.");
        foreach (var h in hours.Where(h => !h.IsClosed))
        {
            if (h.CloseTime <= h.OpenTime) throw ValidationException.For("hours", $"{h.DayOfWeek} : l'heure de fermeture doit être après l'ouverture.");
            if ((h.BreakStart is null) != (h.BreakEnd is null)
                || (h.BreakStart is { } bs && h.BreakEnd is { } be && (bs <= h.OpenTime || be >= h.CloseTime || be <= bs)))
                throw ValidationException.For("hours", $"{h.DayOfWeek} : pause invalide.");
        }

        var org = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        foreach (var dto in hours)
        {
            var existing = org.OpeningHours.FirstOrDefault(x => x.DayOfWeek == dto.DayOfWeek);
            if (existing is null) { existing = new OpeningHour { OrganizationId = id, DayOfWeek = dto.DayOfWeek }; org.OpeningHours.Add(existing); }
            existing.IsClosed = dto.IsClosed;
            existing.OpenTime = dto.OpenTime;
            existing.CloseTime = dto.CloseTime;
            existing.BreakStart = dto.BreakStart;
            existing.BreakEnd = dto.BreakEnd;
        }
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    public async Task<MyOrganizationDto> UpdateRulesAsync(Guid id, BookingRulesDto rules, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        if (rules.DepositMode == DepositMode.Percent && rules.DepositValue > 100)
            throw ValidationException.For(nameof(rules.DepositValue), "Le pourcentage d'acompte doit être entre 0 et 100.");
        var org = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        org.BookingType = rules.BookingType;
        org.ExternalBookingEnabled = rules.ExternalBookingEnabled;
        org.ManualValidationRequired = rules.ManualValidationRequired;
        org.CancellationDeadlineHours = rules.CancellationDeadlineHours;
        org.SlotStepMinutes = rules.SlotStepMinutes;
        org.DepositMode = rules.DepositMode;
        org.DepositValue = rules.DepositMode == DepositMode.None ? 0 : rules.DepositValue;
        org.AutoRemindersEnabled = rules.AutoRemindersEnabled;
        if (!string.IsNullOrWhiteSpace(rules.ReminderTemplate)) org.ReminderTemplate = rules.ReminderTemplate.Trim();
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    public async Task<MyOrganizationDto> SubmitVerificationAsync(Guid id, VerificationRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        if (string.IsNullOrWhiteSpace(request.TaxId) && string.IsNullOrWhiteSpace(request.RneNumber))
            throw ValidationException.For(nameof(request.TaxId), "Indiquez le matricule fiscal ou le numéro RNE.");
        var org = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        org.TaxId = request.TaxId?.Trim().ToUpperInvariant();
        org.RneNumber = request.RneNumber?.Trim().ToUpperInvariant();
        if (org.VerificationStatus != VerificationStatus.Verified) org.VerificationStatus = VerificationStatus.Pending;
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    public async Task<MyOrganizationDto> SetPublishedAsync(Guid id, bool published, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        var org = await uow.Organizations.GetDetailsAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        if (published)
        {
            var completion = await ComputeCompletionAsync(org, ct);
            var required = new[] { "entreprise", "prestations", "horaires", "verification" };
            var missing = completion.Steps.Where(s => required.Contains(s.Key) && !s.Done).Select(s => s.Label).ToList();
            if (missing.Count > 0)
                throw new BusinessRuleException("Avant de publier, complétez : " + string.Join(", ", missing) + ".", "profile_incomplete");
        }
        var tracked = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException();
        tracked.IsPublished = published;
        if (published) tracked.PublishedAt ??= clock.UtcNow;
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    /// <param name="kind">logo | cover | photo</param>
    public async Task<MyOrganizationDto> UploadImageAsync(Guid id, string kind, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        var org = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        if (kind == "photo" && org.Photos.Count >= MaxPhotos) throw new BusinessRuleException($"Maximum {MaxPhotos} photos.");
        StoredFile stored;
        try { stored = await storage.SaveImageAsync(content, fileName, contentType, $"org-{id:N}", ct); }
        catch (InvalidFileException ex) { throw new BadRequestException(ex.Message, "invalid_file"); }

        switch (kind)
        {
            case "logo":
                if (org.LogoUrl is not null) await storage.DeleteAsync(org.LogoUrl, ct);
                org.LogoUrl = stored.Url;
                break;
            case "cover":
                if (org.CoverUrl is not null) await storage.DeleteAsync(org.CoverUrl, ct);
                org.CoverUrl = stored.Url;
                break;
            case "photo":
                org.Photos.Add(new OrganizationPhoto { OrganizationId = id, Url = stored.Url, SortOrder = org.Photos.Count });
                break;
            default:
                await storage.DeleteAsync(stored.Url, ct);
                throw new BadRequestException("Type d'image inconnu (logo, cover ou photo).");
        }
        await uow.SaveChangesAsync(ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    public async Task<MyOrganizationDto> DeletePhotoAsync(Guid id, string url, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ownerOnly: true, ct);
        var photo = await uow.OrganizationPhotos.FirstOrDefaultAsync(p => p.OrganizationId == id && p.Url == url, ct)
                    ?? throw new NotFoundException("Photo introuvable.");
        uow.OrganizationPhotos.Remove(photo);
        await uow.SaveChangesAsync(ct);
        await storage.DeleteAsync(url, ct);
        return await GetMyOrganizationAsync(id, ct);
    }

    public async Task<ProfileCompletionDto> GetCompletionAsync(Guid id, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ct: ct);
        var org = await uow.Organizations.GetDetailsAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        return await ComputeCompletionAsync(org, ct);
    }

    internal async Task<ProfileCompletionDto> ComputeCompletionAsync(Organization org, CancellationToken ct)
    {
        var ownerPhoneConfirmed = await uow.OrganizationMembers.QueryNoTracking()
            .AnyAsync(m => m.OrganizationId == org.Id && m.Role == MemberRole.Owner && m.User.PhoneConfirmed, ct);
        var steps = new List<CompletionStepDto>
        {
            new("compte", "Compte vérifié", ownerPhoneConfirmed),
            new("entreprise", "Informations de l'entreprise", !string.IsNullOrWhiteSpace(org.Description) && !string.IsNullOrWhiteSpace(org.Address)),
            new("photos", "Logo et photos", org.LogoUrl is not null || org.CoverUrl is not null || org.Photos.Count > 0),
            new("prestations", "Prestations", org.Services.Any(s => s.IsActive)),
            new("horaires", "Horaires d'ouverture", org.OpeningHours.Any(h => !h.IsClosed)),
            new("verification", "Vérification (matricule fiscal / RNE)", org.VerificationStatus is VerificationStatus.Pending or VerificationStatus.Verified),
            new("publication", "Page publiée", org.IsPublished),
        };
        var percent = (int)Math.Round(steps.Count(s => s.Done) * 100.0 / steps.Count);
        return new ProfileCompletionDto(percent, steps);
    }

    public async Task<PublicLinkDto> GetPublicLinkAsync(Guid id, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ct: ct);
        var org = await uow.Organizations.QueryNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException();
        var url = PublicUrl(org);
        return new PublicLinkDto(url, org.Slug, url, $"Réservez votre rendez-vous chez {org.Name} en ligne, 24h/24 : {url}");
    }

    private string PublicUrl(Organization org) => $"{appOptions.Value.PublicWebUrl.TrimEnd('/')}/b/{org.Slug}";

    // =========================================================== Closed periods (holidays, days off)
    public async Task<IReadOnlyList<ClosedPeriodDto>> GetClosedPeriodsAsync(Guid id, DateTime? fromUtc, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ct: ct);
        var from = fromUtc ?? clock.UtcNow.AddDays(-1);
        var list = await uow.ClosedPeriods.ListAsync(c => c.OrganizationId == id && c.EndUtc >= from, ct);
        return list.OrderBy(c => c.StartUtc).Select(c => c.ToDto()).ToList();
    }

    public async Task<ClosedPeriodDto> AddClosedPeriodAsync(Guid id, CreateClosedPeriodRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ct: ct);
        var start = DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(request.EndUtc, DateTimeKind.Utc);
        if (end <= start) throw ValidationException.For(nameof(request.EndUtc), "La fin doit être après le début.");
        if (request.ResourceId is { } rid && !await uow.Resources.AnyAsync(r => r.Id == rid && r.OrganizationId == id, ct))
            throw ValidationException.For(nameof(request.ResourceId), "Ressource inconnue.");
        var period = new ClosedPeriod { OrganizationId = id, ResourceId = request.ResourceId, StartUtc = start, EndUtc = end, Reason = request.Reason?.Trim() };
        await uow.ClosedPeriods.AddAsync(period, ct);
        await uow.SaveChangesAsync(ct);
        return period.ToDto();
    }

    public async Task DeleteClosedPeriodAsync(Guid id, Guid periodId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(id, ct: ct);
        var p = await uow.ClosedPeriods.FirstOrDefaultAsync(c => c.Id == periodId && c.OrganizationId == id, ct)
                ?? throw new NotFoundException("Période introuvable.");
        uow.ClosedPeriods.Remove(p);
        await uow.SaveChangesAsync(ct);
    }

    // =========================================================== Admin
    public async Task<IReadOnlyList<MyOrganizationDto>> GetPendingVerificationsAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin)) throw new ForbiddenException();
        var ids = await uow.Organizations.QueryNoTracking()
            .Where(o => o.VerificationStatus == VerificationStatus.Pending).OrderBy(o => o.UpdatedAt).Select(o => o.Id).Take(50).ToListAsync(ct);
        var list = new List<MyOrganizationDto>();
        foreach (var id in ids) list.Add(await GetMyOrganizationAsync(id, ct));
        return list;
    }

    public async Task SetVerificationStatusAsync(Guid id, bool approved, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin)) throw new ForbiddenException();
        var org = await uow.Organizations.GetForEditAsync(id, ct) ?? throw new NotFoundException("Entreprise introuvable.");
        org.VerificationStatus = approved ? VerificationStatus.Verified : VerificationStatus.Rejected;
        await notifications.NotifyOrganizationAsync(id, NotificationType.General,
            approved ? "Entreprise vérifiée" : "Vérification refusée",
            approved ? $"{org.Name} affiche maintenant le badge « Vérifié »." : "Vos informations n'ont pas pu être vérifiées. Contactez le support.",
            null, ct);
        await uow.SaveChangesAsync(ct);
    }
}
