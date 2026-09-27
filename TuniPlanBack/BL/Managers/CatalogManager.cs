using BL.Mapping;
using Common.Exceptions;
using DAO.Interfaces;
using DTOs.Catalog;
using Entities;
using Microsoft.EntityFrameworkCore;
using OperationStorage;

namespace BL.Managers;

/// <summary>Services (prestations), resources (team, rooms, vehicles) and promotions of an organization.</summary>
public interface ICatalogManager
{
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid organizationId, bool includeInactive, CancellationToken ct = default);
    Task<ServiceDto> CreateServiceAsync(Guid organizationId, UpsertServiceRequest request, CancellationToken ct = default);
    Task<ServiceDto> UpdateServiceAsync(Guid organizationId, Guid serviceId, UpsertServiceRequest request, CancellationToken ct = default);
    Task DeleteServiceAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default);

    Task<IReadOnlyList<ResourceDto>> GetResourcesAsync(Guid organizationId, bool includeInactive, CancellationToken ct = default);
    Task<ResourceDto> CreateResourceAsync(Guid organizationId, UpsertResourceRequest request, CancellationToken ct = default);
    Task<ResourceDto> UpdateResourceAsync(Guid organizationId, Guid resourceId, UpsertResourceRequest request, CancellationToken ct = default);
    Task<ResourceDto> UploadResourcePhotoAsync(Guid organizationId, Guid resourceId, Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task DeleteResourceAsync(Guid organizationId, Guid resourceId, CancellationToken ct = default);

    Task<IReadOnlyList<PromotionDto>> GetPromotionsAsync(Guid organizationId, bool activeOnly, CancellationToken ct = default);
    Task<PromotionDto> CreatePromotionAsync(Guid organizationId, UpsertPromotionRequest request, CancellationToken ct = default);
    Task<PromotionDto> UpdatePromotionAsync(Guid organizationId, Guid promotionId, UpsertPromotionRequest request, CancellationToken ct = default);
    Task DeletePromotionAsync(Guid organizationId, Guid promotionId, CancellationToken ct = default);
}

public sealed class CatalogManager(IUnitOfWork uow, IOrganizationAccess access, IFileStorage storage) : ICatalogManager
{
    // ------------------------------------------------ Services
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid organizationId, bool includeInactive, CancellationToken ct = default)
    {
        if (includeInactive) await access.EnsureMemberAsync(organizationId, ct: ct);
        var list = await uow.Services.GetByOrganizationAsync(organizationId, !includeInactive, ct);
        return list.Select(s => s.ToDto()).ToList();
    }

    public async Task<ServiceDto> CreateServiceAsync(Guid organizationId, UpsertServiceRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var service = new Service { OrganizationId = organizationId };
        await ApplyAsync(service, organizationId, request, ct);
        await uow.Services.AddAsync(service, ct);
        await uow.SaveChangesAsync(ct);
        return service.ToDto();
    }

    public async Task<ServiceDto> UpdateServiceAsync(Guid organizationId, Guid serviceId, UpsertServiceRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var service = await uow.Services.GetWithResourcesAsync(serviceId, ct);
        if (service is null || service.OrganizationId != organizationId) throw new NotFoundException("Prestation introuvable.");
        await ApplyAsync(service, organizationId, request, ct);
        await uow.SaveChangesAsync(ct);
        return service.ToDto();
    }

    public async Task DeleteServiceAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var service = await uow.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId, ct)
                      ?? throw new NotFoundException("Prestation introuvable.");
        var now = DateTime.UtcNow;
        if (await uow.Appointments.AnyAsync(a => a.ServiceId == serviceId && a.StartUtc > now
                && (a.Status == Entities.Enums.AppointmentStatus.Pending || a.Status == Entities.Enums.AppointmentStatus.Confirmed), ct))
            throw new BusinessRuleException("Cette prestation a des rendez-vous à venir : désactivez-la plutôt que de la supprimer.");
        uow.Services.Remove(service); // soft delete
        await uow.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Service s, Guid organizationId, UpsertServiceRequest r, CancellationToken ct)
    {
        var resourceIds = r.ResourceIds.Distinct().ToList();
        if (resourceIds.Count > 0)
        {
            var valid = await uow.Resources.CountAsync(x => resourceIds.Contains(x.Id) && x.OrganizationId == organizationId, ct);
            if (valid != resourceIds.Count) throw ValidationException.For(nameof(r.ResourceIds), "Ressource inconnue.");
        }
        s.Name = r.Name.Trim();
        s.Description = r.Description?.Trim();
        s.Category = r.Category?.Trim();
        s.DurationMinutes = r.DurationMinutes;
        s.Price = decimal.Round(r.Price, 3);
        s.RequiresDeposit = r.RequiresDeposit;
        s.IsActive = r.IsActive;
        s.SortOrder = r.SortOrder;

        var toRemove = s.ServiceResources.Where(sr => !resourceIds.Contains(sr.ResourceId)).ToList();
        foreach (var sr in toRemove) s.ServiceResources.Remove(sr);
        foreach (var id in resourceIds.Where(id => s.ServiceResources.All(sr => sr.ResourceId != id)))
            s.ServiceResources.Add(new ServiceResource { ServiceId = s.Id, ResourceId = id });
    }

    // ------------------------------------------------ Resources
    public async Task<IReadOnlyList<ResourceDto>> GetResourcesAsync(Guid organizationId, bool includeInactive, CancellationToken ct = default)
    {
        if (includeInactive) await access.EnsureMemberAsync(organizationId, ct: ct);
        var list = await uow.Resources.ListAsync(r => r.OrganizationId == organizationId && (includeInactive || r.IsActive), ct);
        return list.OrderBy(r => r.Name).Select(r => r.ToDto()).ToList();
    }

    public async Task<ResourceDto> CreateResourceAsync(Guid organizationId, UpsertResourceRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var r = new Resource { OrganizationId = organizationId };
        Apply(r, request);
        await uow.Resources.AddAsync(r, ct);
        await uow.SaveChangesAsync(ct);
        return r.ToDto();
    }

    public async Task<ResourceDto> UpdateResourceAsync(Guid organizationId, Guid resourceId, UpsertResourceRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var r = await FindResourceAsync(organizationId, resourceId, ct);
        Apply(r, request);
        await uow.SaveChangesAsync(ct);
        return r.ToDto();
    }

    public async Task<ResourceDto> UploadResourcePhotoAsync(Guid organizationId, Guid resourceId, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var r = await FindResourceAsync(organizationId, resourceId, ct);
        try
        {
            var stored = await storage.SaveImageAsync(content, fileName, contentType, "team", ct);
            if (r.PhotoUrl is not null) await storage.DeleteAsync(r.PhotoUrl, ct);
            r.PhotoUrl = stored.Url;
        }
        catch (InvalidFileException ex) { throw new BadRequestException(ex.Message, "invalid_file"); }
        await uow.SaveChangesAsync(ct);
        return r.ToDto();
    }

    public async Task DeleteResourceAsync(Guid organizationId, Guid resourceId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var r = await FindResourceAsync(organizationId, resourceId, ct);
        var now = DateTime.UtcNow;
        if (await uow.Appointments.AnyAsync(a => a.ResourceId == resourceId && a.StartUtc > now
                && (a.Status == Entities.Enums.AppointmentStatus.Pending || a.Status == Entities.Enums.AppointmentStatus.Confirmed), ct))
            throw new BusinessRuleException("Cette ressource a des rendez-vous à venir : désactivez-la plutôt.");
        uow.Resources.Remove(r);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<Resource> FindResourceAsync(Guid organizationId, Guid resourceId, CancellationToken ct) =>
        await uow.Resources.FirstOrDefaultAsync(x => x.Id == resourceId && x.OrganizationId == organizationId, ct)
        ?? throw new NotFoundException("Ressource introuvable.");

    private static void Apply(Resource r, UpsertResourceRequest req)
    {
        r.Name = req.Name.Trim();
        r.Title = req.Title?.Trim();
        if (req.PhotoUrl is not null) r.PhotoUrl = req.PhotoUrl;
        r.Type = req.Type;
        r.IsActive = req.IsActive;
    }

    // ------------------------------------------------ Promotions
    public async Task<IReadOnlyList<PromotionDto>> GetPromotionsAsync(Guid organizationId, bool activeOnly, CancellationToken ct = default)
    {
        if (!activeOnly) await access.EnsureMemberAsync(organizationId, ct: ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var list = await uow.Promotions.ListAsync(p => p.OrganizationId == organizationId
                                                       && (!activeOnly || (p.IsActive && p.ValidTo >= today)), ct);
        return list.OrderByDescending(p => p.ValidFrom).Select(p => p.ToDto()).ToList();
    }

    public async Task<PromotionDto> CreatePromotionAsync(Guid organizationId, UpsertPromotionRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var p = new Promotion { OrganizationId = organizationId };
        await ApplyAsync(p, organizationId, request, ct);
        await uow.Promotions.AddAsync(p, ct);
        await uow.SaveChangesAsync(ct);
        return p.ToDto();
    }

    public async Task<PromotionDto> UpdatePromotionAsync(Guid organizationId, Guid promotionId, UpsertPromotionRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var p = await uow.Promotions.FirstOrDefaultAsync(x => x.Id == promotionId && x.OrganizationId == organizationId, ct)
                ?? throw new NotFoundException("Offre introuvable.");
        await ApplyAsync(p, organizationId, request, ct);
        await uow.SaveChangesAsync(ct);
        return p.ToDto();
    }

    public async Task DeletePromotionAsync(Guid organizationId, Guid promotionId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ownerOnly: true, ct);
        var p = await uow.Promotions.FirstOrDefaultAsync(x => x.Id == promotionId && x.OrganizationId == organizationId, ct)
                ?? throw new NotFoundException("Offre introuvable.");
        uow.Promotions.Remove(p);
        await uow.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Promotion p, Guid organizationId, UpsertPromotionRequest r, CancellationToken ct)
    {
        if (r.ValidTo < r.ValidFrom) throw ValidationException.For(nameof(r.ValidTo), "La date de fin doit être après la date de début.");
        if (r.StartTime is not null && r.EndTime is not null && r.EndTime <= r.StartTime)
            throw ValidationException.For(nameof(r.EndTime), "L'heure de fin doit être après l'heure de début.");
        if (r.ServiceId is { } sid && !await uow.Services.AnyAsync(s => s.Id == sid && s.OrganizationId == organizationId, ct))
            throw ValidationException.For(nameof(r.ServiceId), "Prestation inconnue.");
        p.Title = r.Title.Trim();
        p.Description = r.Description?.Trim();
        p.DiscountPercent = r.DiscountPercent;
        p.ServiceId = r.ServiceId;
        p.DaysOfWeekMask = r.DaysOfWeekMask;
        p.StartTime = r.StartTime;
        p.EndTime = r.EndTime;
        p.ValidFrom = r.ValidFrom;
        p.ValidTo = r.ValidTo;
        p.IsLastMinute = r.IsLastMinute;
        p.IsActive = r.IsActive;
    }
}

/// <summary>Finds the best promotion applicable to a service at a given local date/time.</summary>
public static class PromotionRules
{
    public static int BestDiscount(IEnumerable<Promotion> promotions, Guid serviceId, DateTime localStart, DateTime? utcNow = null, DateTime? startUtc = null)
    {
        var date = DateOnly.FromDateTime(localStart);
        var time = TimeOnly.FromDateTime(localStart);
        var best = 0;
        foreach (var p in promotions.Where(p => p.IsActive))
        {
            if (p.ServiceId is not null && p.ServiceId != serviceId) continue;
            if (date < p.ValidFrom || date > p.ValidTo) continue;
            if (p.DaysOfWeekMask != 0 && (p.DaysOfWeekMask & (1 << (int)date.DayOfWeek)) == 0) continue;
            if (p.StartTime is { } st && time < st) continue;
            if (p.EndTime is { } et && time >= et) continue;
            if (p.IsLastMinute && (utcNow is null || startUtc is null || startUtc.Value - utcNow.Value > TimeSpan.FromHours(24))) continue;
            best = Math.Max(best, p.DiscountPercent);
        }
        return best;
    }
}
