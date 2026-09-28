using System.ComponentModel.DataAnnotations;
using DTOs.Catalog;
using DTOs.Common;
using DTOs.Reviews;
using Entities.Enums;

namespace DTOs.Organizations;

public sealed record OrganizationCardDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public BusinessCategory Category { get; init; }
    public string? Subcategory { get; init; }
    public string City { get; init; } = default!;
    public string Governorate { get; init; } = default!;
    public string? CoverUrl { get; init; }
    public string? LogoUrl { get; init; }
    public double RatingAverage { get; init; }
    public int ReviewCount { get; init; }
    public decimal? PriceFrom { get; init; }
    public DateTime? NextSlotUtc { get; init; }
    public bool OpenNow { get; init; }
    public bool IsFavorite { get; init; }
    public bool HasPromotion { get; init; }
    public bool IsVerified { get; init; }
    public BookingType BookingType { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? DistanceKm { get; init; }
}

public sealed record OrganizationSearchQuery : PageQuery
{
    [StringLength(100)] public string? Q { get; init; }
    public BusinessCategory? Category { get; init; }
    [StringLength(50)] public string? Governorate { get; init; }
    [StringLength(80)] public string? City { get; init; }
    /// <summary>Only businesses with at least one free slot that day.</summary>
    public DateOnly? Date { get; init; }
    public bool OpenNow { get; init; }
    [Range(0, 5)] public double? MinRating { get; init; }
    [Range(0, 100_000)] public decimal? MaxPrice { get; init; }
    /// <summary>relevance | rating | price | distance</summary>
    [RegularExpression("^(relevance|rating|price|distance)$")] public string Sort { get; init; } = "relevance";
    [Range(-90, 90)] public double? Lat { get; init; }
    [Range(-180, 180)] public double? Lng { get; init; }
}

public sealed record OpeningHourDto
{
    public DayOfWeek DayOfWeek { get; init; }
    public bool IsClosed { get; init; }
    public TimeOnly OpenTime { get; init; }
    public TimeOnly CloseTime { get; init; }
    public TimeOnly? BreakStart { get; init; }
    public TimeOnly? BreakEnd { get; init; }
}

public sealed record OrganizationDetailsDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public BusinessCategory Category { get; init; }
    public string? Subcategory { get; init; }
    public string? Description { get; init; }
    public string Governorate { get; init; } = default!;
    public string City { get; init; } = default!;
    public string Address { get; init; } = default!;
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? Phone { get; init; }
    public string? WhatsApp { get; init; }
    public string? Email { get; init; }
    public string? LogoUrl { get; init; }
    public string? CoverUrl { get; init; }
    public IReadOnlyList<string> Photos { get; init; } = [];
    public IReadOnlyList<OpeningHourDto> OpeningHours { get; init; } = [];
    public bool OpenNow { get; init; }
    public IReadOnlyList<ResourceDto> Team { get; init; } = [];
    public IReadOnlyList<ServiceDto> Services { get; init; } = [];
    public IReadOnlyList<PromotionDto> Promotions { get; init; } = [];
    public decimal? PriceFrom { get; init; }
    public BookingType BookingType { get; init; }
    public bool ManualValidationRequired { get; init; }
    public int CancellationDeadlineHours { get; init; }
    public DepositMode DepositMode { get; init; }
    public decimal DepositValue { get; init; }
    public bool IsVerified { get; init; }
    public bool IsFavorite { get; init; }
    public ReviewSummaryDto ReviewSummary { get; init; } = default!;
    public IReadOnlyList<ReviewDto> LatestReviews { get; init; } = [];
    public IReadOnlyList<SlotDto> NextSlots { get; init; } = [];
}

public sealed record UpsertOrganizationRequest
{
    [Required, StringLength(150, MinimumLength = 2)] public string Name { get; init; } = default!;
    [Required] public BusinessCategory Category { get; init; }
    [StringLength(100)] public string? Subcategory { get; init; }
    [StringLength(500)] public string? Description { get; init; }
    [Required, StringLength(50)] public string Governorate { get; init; } = default!;
    [Required, StringLength(80)] public string City { get; init; } = default!;
    [Required, StringLength(250)] public string Address { get; init; } = default!;
    [Range(-90, 90)] public double? Latitude { get; init; }
    [Range(-180, 180)] public double? Longitude { get; init; }
    [StringLength(20)] public string? Phone { get; init; }
    [StringLength(20)] public string? WhatsApp { get; init; }
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
}

public sealed record BookingRulesDto
{
    public BookingType BookingType { get; init; }
    public bool ExternalBookingEnabled { get; init; } = true;
    public bool ManualValidationRequired { get; init; } = true;
    [Range(0, 168)] public int CancellationDeadlineHours { get; init; } = 24;
    [Range(5, 120)] public int SlotStepMinutes { get; init; } = 15;
    public DepositMode DepositMode { get; init; }
    [Range(0, 100_000)] public decimal DepositValue { get; init; }
    public bool AutoRemindersEnabled { get; init; } = true;
    [StringLength(500)] public string? ReminderTemplate { get; init; }
}

public sealed record VerificationRequest
{
    [StringLength(50)] public string? TaxId { get; init; }
    [StringLength(50)] public string? RneNumber { get; init; }
}

public sealed record CompletionStepDto(string Key, string Label, bool Done);

public sealed record ProfileCompletionDto(int Percent, IReadOnlyList<CompletionStepDto> Steps);

public sealed record MyOrganizationDto
{
    public OrganizationDetailsDto Details { get; init; } = default!;
    public BookingRulesDto Rules { get; init; } = default!;
    public VerificationStatus VerificationStatus { get; init; }
    public string? TaxId { get; init; }
    public string? RneNumber { get; init; }
    public bool IsPublished { get; init; }
    public ProfileCompletionDto Completion { get; init; } = default!;
    public string PublicUrl { get; init; } = default!;
}

public sealed record ClosedPeriodDto(Guid Id, Guid? ResourceId, DateTime StartUtc, DateTime EndUtc, string? Reason);

public sealed record CreateClosedPeriodRequest
{
    public Guid? ResourceId { get; init; }
    [Required] public DateTime StartUtc { get; init; }
    [Required] public DateTime EndUtc { get; init; }
    [StringLength(200)] public string? Reason { get; init; }
}

public sealed record PublicLinkDto(string Url, string Slug, string QrPayload, string ShareText);

public sealed record TransferOrganizationRequest
{
    /// <summary>Email or phone number of the new owner (an existing account with business mode enabled).</summary>
    [Required, StringLength(256)] public string NewOwnerIdentifier { get; init; } = default!;
}
