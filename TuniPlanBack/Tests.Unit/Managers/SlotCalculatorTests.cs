using BL.Availability;
using Common.Helpers;
using Entities;

namespace Tests.Unit.Managers;

public class SlotCalculatorTests
{
    private static readonly TimeZoneInfo Tz = TimeZoneHelper.Find("Africa/Tunis");
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static List<OpeningHour> Hours(bool withBreak = true) =>
    [
        new()
        {
            DayOfWeek = DayOfWeek.Monday, OpenTime = new TimeOnly(9, 0), CloseTime = new TimeOnly(12, 0),
            BreakStart = withBreak ? new TimeOnly(10, 0) : null, BreakEnd = withBreak ? new TimeOnly(11, 0) : null
        },
        new() { DayOfWeek = DayOfWeek.Sunday, IsClosed = true }
    ];

    private static SlotComputationInput Input(IReadOnlyCollection<BusyInterval>? busy = null, IReadOnlyList<Guid>? resources = null,
        IReadOnlyCollection<BusyInterval>? closed = null, bool withBreak = true) =>
        new(Tz, Hours(withBreak), busy ?? [], closed ?? [], resources ?? [], 30, 30, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Generates_slots_around_the_break()
    {
        var day = SlotCalculator.ComputeDay(Monday, Input());
        Assert.True(day.IsOpen);
        Assert.Equal(new[] { "09:00", "09:30", "11:00", "11:30" }, day.Slots.Select(s => s.LocalTime).ToArray());
        Assert.All(day.Slots, s => Assert.Equal("am", s.Period));
    }

    [Fact]
    public void Closed_day_has_no_slots()
    {
        var day = SlotCalculator.ComputeDay(Monday.AddDays(-1), Input());
        Assert.False(day.IsOpen);
        Assert.Empty(day.Slots);
    }

    [Fact]
    public void Busy_appointment_removes_slot_without_resources()
    {
        var nineUtc = TimeZoneHelper.ToUtc(Monday, new TimeOnly(9, 0), Tz);
        var day = SlotCalculator.ComputeDay(Monday, Input(busy: [new BusyInterval(nineUtc, nineUtc.AddMinutes(30), null)]));
        Assert.DoesNotContain(day.Slots, s => s.LocalTime == "09:00");
        Assert.Contains(day.Slots, s => s.LocalTime == "09:30");
    }

    [Fact]
    public void Slot_stays_free_while_another_resource_is_available()
    {
        var lina = Guid.NewGuid();
        var sarra = Guid.NewGuid();
        var nineUtc = TimeZoneHelper.ToUtc(Monday, new TimeOnly(9, 0), Tz);
        var day = SlotCalculator.ComputeDay(Monday, Input(busy: [new BusyInterval(nineUtc, nineUtc.AddMinutes(30), lina)], resources: [lina, sarra]));
        var nine = Assert.Single(day.Slots, s => s.LocalTime == "09:00");
        Assert.Equal(new[] { sarra }, nine.ResourceIds);
    }

    [Fact]
    public void Organization_closed_period_blocks_everything()
    {
        var start = TimeZoneHelper.ToUtc(Monday, new TimeOnly(0, 0), Tz);
        var day = SlotCalculator.ComputeDay(Monday, Input(closed: [new BusyInterval(start, start.AddDays(1), null)]));
        Assert.Empty(day.Slots);
    }

    [Fact]
    public void Past_slots_are_not_offered()
    {
        var now = TimeZoneHelper.ToUtc(Monday, new TimeOnly(9, 15), Tz);
        var input = Input(withBreak: false) with { NowUtc = now };
        var day = SlotCalculator.ComputeDay(Monday, input);
        // lead time 30 min → first slot 10:00
        Assert.Equal("10:00", day.Slots.First().LocalTime);
    }
}
