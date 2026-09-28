using System.ComponentModel.DataAnnotations;
using Entities.Enums;

namespace DTOs.Catalog;

public sealed record ServiceDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public int DurationMinutes { get; init; }
    public decimal Price { get; init; }
    /// <summary>Price after the best active promotion, if any.</summary>
    public decimal? DiscountedPrice { get; init; }
    public bool RequiresDeposit { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<Guid> ResourceIds { get; init; } = [];
}

public sealed record UpsertServiceRequest
{
    [Required, StringLength(150, MinimumLength = 2)] public string Name { get; init; } = default!;
    [StringLength(1000)] public string? Description { get; init; }
    [StringLength(80)] public string? Category { get; init; }
    [Range(5, 43_200)] public int DurationMinutes { get; init; } = 30;
    [Range(0, 100_000)] public decimal Price { get; init; }
    public bool RequiresDeposit { get; init; }
    public bool IsActive { get; init; } = true;
    public int SortOrder { get; init; }
    public IReadOnlyList<Guid> ResourceIds { get; init; } = [];
}

public sealed record ResourceDto(Guid Id, string Name, string? Title, string? PhotoUrl, ResourceType Type, bool IsActive);

public sealed record UpsertResourceRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; init; } = default!;
    [StringLength(120)] public string? Title { get; init; }
    [StringLength(512)] public string? PhotoUrl { get; init; }
    public ResourceType Type { get; init; } = ResourceType.Person;
    public bool IsActive { get; init; } = true;
}

public sealed record SlotDto(DateTime StartUtc, DateTime EndUtc, DateOnly LocalDate, string LocalTime, string Period, IReadOnlyList<Guid> ResourceIds);

public sealed record DayAvailabilityDto(DateOnly Date, bool IsOpen, IReadOnlyList<SlotDto> Slots);

public sealed record AvailabilityQuery
{
    /// <summary>Optional: when omitted, the first active service of the business (by display order) is used.</summary>
    public Guid? ServiceId { get; init; }
    public Guid? ResourceId { get; init; }
    public DateOnly? From { get; init; }
    [Range(1, 31)] public int Days { get; init; } = 7;
}

public sealed record PromotionDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = default!;
    public string? Description { get; init; }
    public int DiscountPercent { get; init; }
    public Guid? ServiceId { get; init; }
    public int DaysOfWeekMask { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public DateOnly ValidFrom { get; init; }
    public DateOnly ValidTo { get; init; }
    public bool IsLastMinute { get; init; }
    public bool IsActive { get; init; }
}

public sealed record UpsertPromotionRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Title { get; init; } = default!;
    [StringLength(500)] public string? Description { get; init; }
    [Range(1, 90)] public int DiscountPercent { get; init; }
    public Guid? ServiceId { get; init; }
    [Range(0, 127)] public int DaysOfWeekMask { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public DateOnly ValidFrom { get; init; }
    public DateOnly ValidTo { get; init; }
    public bool IsLastMinute { get; init; }
    public bool IsActive { get; init; } = true;
}
