using System.ComponentModel.DataAnnotations;
using DTOs.Common;

namespace DTOs.Reviews;

public sealed record ReviewDto
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public Guid AppointmentId { get; init; }
    /// <summary>"Prénom N." or "Client anonyme" when <see cref="IsAnonymous"/> (also for the business).</summary>
    public string ClientName { get; init; } = default!;
    public bool IsAnonymous { get; init; }
    /// <summary>True when the connected user wrote this review (can edit / delete it).</summary>
    public bool IsMine { get; init; }
    public int Rating { get; init; }
    public int? RatingWelcome { get; init; }
    public int? RatingPunctuality { get; init; }
    public int? RatingQuality { get; init; }
    public int? RatingValue { get; init; }
    public string Comment { get; init; } = default!;
    public IReadOnlyList<string> PhotoUrls { get; init; } = [];
    public string? ServiceName { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Last edit by the author, null if never edited.</summary>
    public DateTime? UpdatedAt { get; init; }
    /// <summary>Until when the author can still edit it (null when not <see cref="IsMine"/>).</summary>
    public DateTime? EditableUntil { get; init; }
    public string? OwnerReply { get; init; }
    public DateTime? OwnerReplyAt { get; init; }
    public bool Verified { get; init; } = true;
}

public sealed record ReviewSummaryDto
{
    public double Average { get; init; }
    public int Count { get; init; }
    /// <summary>Key = stars (1..5), value = number of reviews.</summary>
    public IReadOnlyDictionary<int, int> Distribution { get; init; } = new Dictionary<int, int>();
    public double? AverageWelcome { get; init; }
    public double? AveragePunctuality { get; init; }
    public double? AverageQuality { get; init; }
    public double? AverageValue { get; init; }
}

public sealed record ReviewQuery : PageQuery
{
    /// <summary>recent | best | worst</summary>
    [RegularExpression("^(recent|best|worst)$")] public string Sort { get; init; } = "recent";
    public bool WithPhotos { get; init; }
    public Guid? ServiceId { get; init; }
}

public sealed record CreateReviewRequest : ReviewContentRequest
{
    [Required] public Guid AppointmentId { get; init; }
}

/// <summary>PUT /api/reviews/{id}: same fields as the creation, without the appointment.</summary>
public sealed record UpdateReviewRequest : ReviewContentRequest;

public abstract record ReviewContentRequest
{
    [Range(1, 5)] public int Rating { get; init; }
    [Range(1, 5)] public int? RatingWelcome { get; init; }
    [Range(1, 5)] public int? RatingPunctuality { get; init; }
    [Range(1, 5)] public int? RatingQuality { get; init; }
    [Range(1, 5)] public int? RatingValue { get; init; }
    [Required, StringLength(2000, MinimumLength = 20)] public string Comment { get; init; } = default!;
    /// <summary>Up to 3 URLs returned by POST /api/reviews/photos.</summary>
    [MaxLength(3)] public IReadOnlyList<string> PhotoUrls { get; init; } = [];
    /// <summary>Hide the author's name: shown as "Client anonyme" everywhere.</summary>
    public bool IsAnonymous { get; init; }
}

public sealed record ReplyReviewRequest
{
    [Required, StringLength(2000, MinimumLength = 2)] public string Reply { get; init; } = default!;
}

public sealed record ReportReviewRequest
{
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; init; } = default!;
}

public sealed record BusinessReviewDto
{
    public ReviewDto Review { get; init; } = default!;
    public bool IsNew { get; init; }
    public bool IsReported { get; init; }
}

public sealed record ReviewPhotoDto(string Url);

/// <summary>Moderation view of a reported review.</summary>
public sealed record AdminReviewDto
{
    public ReviewDto Review { get; init; } = default!;
    /// <summary>Real author, even for an anonymous review (admins only).</summary>
    public Guid ClientUserId { get; init; }
    public string? ReportReason { get; init; }
    public bool IsHidden { get; init; }
}
