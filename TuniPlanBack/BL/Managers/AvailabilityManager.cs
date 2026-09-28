using BL.Availability;
using BL.Interfaces;
using BL.Options;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Catalog;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BL.Managers;

public interface IAvailabilityManager
{
    Task<IReadOnlyList<DayAvailabilityDto>> GetAvailabilityAsync(Guid organizationId, AvailabilityQuery query, CancellationToken ct = default);

    /// <summary>Next free slots (all days), used for "Prochains créneaux" and the AI secretary.</summary>
    Task<IReadOnlyList<SlotDto>> GetNextSlotsAsync(Organization organization, Service service, int count, int days = 14,
        Guid? resourceId = null, CancellationToken ct = default);

    /// <summary>Returns the free resources for an exact slot, or null if the slot is not bookable.</summary>
    Task<IReadOnlyList<Guid>?> CheckSlotAsync(Organization organization, Service service, DateTime startUtc, Guid? resourceId,
        Guid? excludeAppointmentId = null, CancellationToken ct = default);
}

public sealed class AvailabilityManager(IUnitOfWork uow, ICurrentUser currentUser, IClock clock, IOptions<AppOptions> appOptions) : IAvailabilityManager
{
    public async Task<IReadOnlyList<DayAvailabilityDto>> GetAvailabilityAsync(Guid organizationId, AvailabilityQuery query, CancellationToken ct = default)
    {
        var org = await uow.Organizations.QueryNoTracking().Include(o => o.OpeningHours)
                      .FirstOrDefaultAsync(o => o.Id == organizationId, ct)
                  ?? throw new NotFoundException("Entreprise introuvable.");
        // Unpublished businesses are invisible to the public (same rule as the business page)
        if (!org.IsPublished && !await CanSeeUnpublishedAsync(org.Id, ct)) throw new NotFoundException("Entreprise introuvable.");

        var services = uow.Services.QueryNoTracking().Include(s => s.ServiceResources)
            .Where(s => s.OrganizationId == organizationId && s.IsActive);
        var service = query.ServiceId is { } serviceId
            ? await services.FirstOrDefaultAsync(s => s.Id == serviceId, ct) ?? throw new NotFoundException("Prestation introuvable.")
            : await services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).FirstOrDefaultAsync(ct);
        if (service is null) return []; // no active service yet: nothing can be booked

        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(clock.UtcNow, tz));
        var from = query.From is { } f && f > today ? f : today;
        var horizon = today.AddDays(appOptions.Value.BookingHorizonDays);
        var days = Math.Clamp(query.Days, 1, 31);
        if (from > horizon) return [];
        return await ComputeAsync(org, service, from, days, query.ResourceId, null, ct);
    }

    private async Task<bool> CanSeeUnpublishedAsync(Guid orgId, CancellationToken ct) =>
        currentUser.UserId is { } uid && (currentUser.IsInRole(Roles.Admin) || await uow.Organizations.IsMemberAsync(orgId, uid, null, ct));

    public async Task<IReadOnlyList<SlotDto>> GetNextSlotsAsync(Organization organization, Service service, int count, int days = 14,
        Guid? resourceId = null, CancellationToken ct = default)
    {
        var tz = TimeZoneHelper.Find(organization.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(clock.UtcNow, tz));
        var result = await ComputeAsync(organization, service, today, days, resourceId, null, ct);
        return result.SelectMany(d => d.Slots).Take(count).ToList();
    }

    public async Task<IReadOnlyList<Guid>?> CheckSlotAsync(Organization organization, Service service, DateTime startUtc, Guid? resourceId,
        Guid? excludeAppointmentId = null, CancellationToken ct = default)
    {
        var tz = TimeZoneHelper.Find(organization.TimeZoneId);
        var utc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var date = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(utc, tz));
        var day = (await ComputeAsync(organization, service, date, 1, resourceId, excludeAppointmentId, ct)).FirstOrDefault();
        var slot = day?.Slots.FirstOrDefault(s => s.StartUtc == utc);
        return slot?.ResourceIds;
    }

    private async Task<IReadOnlyList<DayAvailabilityDto>> ComputeAsync(Organization org, Service service, DateOnly from, int days,
        Guid? resourceId, Guid? excludeAppointmentId, CancellationToken ct)
    {
        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        var hours = org.OpeningHours.Count > 0
            ? org.OpeningHours.ToList()
            : await uow.OpeningHours.ListAsync(h => h.OrganizationId == org.Id, ct);

        var rangeStart = TimeZoneHelper.ToUtc(from, TimeOnly.MinValue, tz);
        var rangeEnd = TimeZoneHelper.ToUtc(from.AddDays(days), TimeOnly.MinValue, tz).AddMinutes(service.DurationMinutes);

        var busy = (await uow.Appointments.GetBusyAsync(org.Id, rangeStart.AddDays(-2), rangeEnd, ct))
            .Where(a => a.Id != excludeAppointmentId && org.BookingType != BookingType.Queue)
            .Select(a => new BusyInterval(a.StartUtc, a.EndUtc, a.ResourceId)).ToList();
        var closed = (await uow.ClosedPeriods.ListAsync(c => c.OrganizationId == org.Id && c.StartUtc < rangeEnd && c.EndUtc > rangeStart, ct))
            .Select(c => new BusyInterval(c.StartUtc, c.EndUtc, c.ResourceId)).ToList();

        var activeResourceIds = await uow.Resources.QueryNoTracking()
            .Where(r => r.OrganizationId == org.Id && r.IsActive).Select(r => r.Id).ToListAsync(ct);
        var eligible = service.ServiceResources.Select(sr => sr.ResourceId).Where(activeResourceIds.Contains).ToList();
        if (resourceId is { } rid)
        {
            if (eligible.Count > 0 && !eligible.Contains(rid)) throw new BadRequestException("Ce membre de l'équipe ne propose pas cette prestation.");
            eligible = [rid];
        }

        var result = new List<DayAvailabilityDto>(days);
        for (var i = 0; i < days; i++)
        {
            var date = from.AddDays(i);
            if (org.BookingType == BookingType.Rental)
            {
                result.Add(RentalDay(date, tz, hours, busy, closed, eligible));
                continue;
            }
            var input = new SlotComputationInput(tz, hours, busy, closed, eligible, service.DurationMinutes,
                org.SlotStepMinutes, clock.UtcNow, org.BookingType == BookingType.Queue ? 0 : 30);
            result.Add(SlotCalculator.ComputeDay(date, input));
        }
        return result;
    }

    /// <summary>Rental: one "slot" per day listing the vehicles free for the whole day.</summary>
    private DayAvailabilityDto RentalDay(DateOnly date, TimeZoneInfo tz, IReadOnlyCollection<OpeningHour> hours,
        IReadOnlyCollection<BusyInterval> busy, IReadOnlyCollection<BusyInterval> closed, IReadOnlyList<Guid> vehicles)
    {
        var start = TimeZoneHelper.ToUtc(date, TimeOnly.MinValue, tz);
        var end = TimeZoneHelper.ToUtc(date.AddDays(1), TimeOnly.MinValue, tz);
        if (start.AddDays(1) < clock.UtcNow) return new DayAvailabilityDto(date, false, []);
        var input = new SlotComputationInput(tz, hours, busy, closed, vehicles, 1440, 1440, clock.UtcNow, 0);
        var free = SlotCalculator.FreeResources(start, end, input);
        return free is null || (vehicles.Count > 0 && free.Count == 0)
            ? new DayAvailabilityDto(date, true, [])
            : new DayAvailabilityDto(date, true, [new SlotDto(start, end, date, "00:00", "am", free)]);
    }
}
