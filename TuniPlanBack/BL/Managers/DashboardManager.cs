using BL.Mapping;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Business;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace BL.Managers;

public interface IDashboardManager
{
    Task<DashboardDto> GetAsync(Guid organizationId, CancellationToken ct = default);
}

public sealed class DashboardManager(IUnitOfWork uow, IOrganizationAccess access, IOrganizationManager organizations, IClock clock) : IDashboardManager
{
    public async Task<DashboardDto> GetAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var org = await uow.Organizations.GetDetailsAsync(organizationId, ct) ?? throw new Common.Exceptions.NotFoundException();
        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(now, tz));
        var todayStart = TimeZoneHelper.ToUtc(today, TimeOnly.MinValue, tz);
        var todayEnd = todayStart.AddDays(1);
        var mondayOffset = ((int)today.DayOfWeek + 6) % 7;
        var weekStartDate = today.AddDays(-mondayOffset);
        var weekStart = TimeZoneHelper.ToUtc(weekStartDate, TimeOnly.MinValue, tz);
        var weekEnd = weekStart.AddDays(7);
        var since30 = now.AddDays(-30);
        var since28 = todayStart.AddDays(-28);

        var appts = uow.Appointments.QueryNoTracking().Where(a => a.OrganizationId == organizationId);
        var active = new[] { AppointmentStatus.Confirmed, AppointmentStatus.Completed, AppointmentStatus.Pending };

        var todayCount = await appts.CountAsync(a => a.StartUtc >= todayStart && a.StartUtc < todayEnd
                                                     && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Completed), ct);
        var pending = await appts.CountAsync(a => a.Status == AppointmentStatus.Pending && a.StartUtc > now, ct);
        var week = await appts.Where(a => a.StartUtc >= weekStart && a.StartUtc < weekEnd && active.Contains(a.Status))
            .Select(a => new { a.StartUtc, a.EndUtc, a.Status, a.Price }).ToListAsync(ct);
        var last30 = await appts.Where(a => a.StartUtc >= since30 && a.StartUtc < now
                                            && (a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.NoShow))
            .Select(a => a.Status).ToListAsync(ct);
        var last4Weeks = await appts.Where(a => a.StartUtc >= since28 && a.StartUtc < todayEnd && active.Contains(a.Status))
            .Select(a => a.StartUtc).ToListAsync(ct);
        var newClients = await uow.OrganizationClients.CountAsync(c => c.OrganizationId == organizationId && c.CreatedAt >= weekStart, ct);

        var next = await uow.Appointments.QueryWithDetails().AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.Status == AppointmentStatus.Confirmed && a.StartUtc >= now)
            .OrderBy(a => a.StartUtc).FirstOrDefaultAsync(ct);

        // Fill rate = booked minutes / open minutes this week (× number of bookable resources, at least 1)
        var capacity = Math.Max(1, org.Resources.Count(r => r.IsActive));
        double openMinutes = 0;
        foreach (var h in org.OpeningHours.Where(h => !h.IsClosed))
        {
            var minutes = (h.CloseTime - h.OpenTime).TotalMinutes;
            if (h.BreakStart is { } bs && h.BreakEnd is { } be) minutes -= (be - bs).TotalMinutes;
            openMinutes += Math.Max(0, minutes);
        }
        var bookedMinutes = week.Where(a => a.Status != AppointmentStatus.Pending).Sum(a => (a.EndUtc - a.StartUtc).TotalMinutes);
        var fillRate = openMinutes <= 0 ? 0 : Math.Min(100, Math.Round(bookedMinutes / (openMinutes * capacity) * 100, 1));
        var noShowRate = last30.Count == 0 ? 0 : Math.Round(last30.Count(s => s == AppointmentStatus.NoShow) * 100.0 / last30.Count, 1);

        var perWeekday = Enum.GetValues<DayOfWeek>()
            .Select(d => new WeekdayCountDto(d, last4Weeks.Count(s => TimeZoneHelper.ToLocal(s, tz).DayOfWeek == d)))
            .OrderBy(x => ((int)x.Day + 6) % 7).ToList();

        return new DashboardDto
        {
            TodayCount = todayCount,
            PendingRequests = pending,
            NextAppointment = next?.ToDto(now, forBusiness: true),
            WeekRevenue = week.Where(a => a.Status == AppointmentStatus.Completed).Sum(a => a.Price),
            WeekAppointments = week.Count(a => a.Status != AppointmentStatus.Pending),
            FillRatePercent = fillRate,
            NoShowRatePercent = noShowRate,
            NewClients = newClients,
            RatingAverage = org.RatingAverage,
            ReviewCount = org.ReviewCount,
            PerWeekday = perWeekday,
            Completion = await organizations.GetCompletionAsync(organizationId, ct)
        };
    }
}
