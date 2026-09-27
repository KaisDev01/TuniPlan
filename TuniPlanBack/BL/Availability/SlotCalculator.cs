using Common.Helpers;
using DTOs.Catalog;
using Entities;

namespace BL.Availability;

public sealed record BusyInterval(DateTime StartUtc, DateTime EndUtc, Guid? ResourceId);

public sealed record SlotComputationInput(
    TimeZoneInfo TimeZone,
    IReadOnlyCollection<OpeningHour> OpeningHours,
    IReadOnlyCollection<BusyInterval> Busy,
    IReadOnlyCollection<BusyInterval> Closed,
    IReadOnlyList<Guid> ResourceIds,
    int DurationMinutes,
    int StepMinutes,
    DateTime NowUtc,
    int MinimumLeadMinutes = 30);

/// <summary>
/// Pure slot computation (no database), unit tested.
/// A slot is free when at least one eligible resource has no booking and no closed period overlapping it.
/// With no resources, the organization has a capacity of one booking at a time.
/// </summary>
public static class SlotCalculator
{
    public static DayAvailabilityDto ComputeDay(DateOnly date, SlotComputationInput input)
    {
        var hours = input.OpeningHours.FirstOrDefault(h => h.DayOfWeek == date.DayOfWeek);
        if (hours is null || hours.IsClosed || hours.CloseTime <= hours.OpenTime)
            return new DayAvailabilityDto(date, false, []);

        var windows = new List<(TimeOnly From, TimeOnly To)>();
        if (hours.BreakStart is { } bs && hours.BreakEnd is { } be && bs > hours.OpenTime && be < hours.CloseTime && be > bs)
        {
            windows.Add((hours.OpenTime, bs));
            windows.Add((be, hours.CloseTime));
        }
        else windows.Add((hours.OpenTime, hours.CloseTime));

        var step = Math.Max(5, input.StepMinutes);
        var duration = Math.Max(5, input.DurationMinutes);
        var earliest = input.NowUtc.AddMinutes(input.MinimumLeadMinutes);
        var slots = new List<SlotDto>();

        foreach (var (from, to) in windows)
        {
            var windowEnd = to.ToTimeSpan().TotalMinutes;
            for (var minute = from.ToTimeSpan().TotalMinutes; minute + duration <= windowEnd; minute += step)
            {
                var localTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
                var startUtc = TimeZoneHelper.ToUtc(date, localTime, input.TimeZone);
                var endUtc = startUtc.AddMinutes(duration);
                if (startUtc < earliest) continue;

                var free = FreeResources(startUtc, endUtc, input);
                if (free is null) continue;

                slots.Add(new SlotDto(startUtc, endUtc, date, localTime.ToString("HH:mm"), localTime.Hour < 12 ? "am" : "pm", free));
            }
        }
        return new DayAvailabilityDto(date, true, slots);
    }

    /// <summary>Returns the free resources (empty list = organization-level slot is free), or null when not free.</summary>
    public static IReadOnlyList<Guid>? FreeResources(DateTime startUtc, DateTime endUtc, SlotComputationInput input)
    {
        bool Overlaps(BusyInterval b) => b.StartUtc < endUtc && b.EndUtc > startUtc;

        // Closed for the whole organization
        if (input.Closed.Any(c => c.ResourceId is null && Overlaps(c))) return null;

        if (input.ResourceIds.Count == 0)
            return input.Busy.Any(Overlaps) ? null : Array.Empty<Guid>();

        var free = input.ResourceIds
            .Where(r => !input.Busy.Any(b => (b.ResourceId == r || b.ResourceId is null) && Overlaps(b)))
            .Where(r => !input.Closed.Any(c => c.ResourceId == r && Overlaps(c)))
            .ToList();
        return free.Count == 0 ? null : free;
    }

    public static bool IsOpenAt(DateTime utc, TimeZoneInfo tz, IEnumerable<OpeningHour> hours)
    {
        var local = TimeZoneHelper.ToLocal(utc, tz);
        var h = hours.FirstOrDefault(x => x.DayOfWeek == local.DayOfWeek);
        if (h is null || h.IsClosed) return false;
        var t = TimeOnly.FromDateTime(local);
        if (t < h.OpenTime || t >= h.CloseTime) return false;
        return !(h.BreakStart is { } bs && h.BreakEnd is { } be && t >= bs && t < be);
    }
}
