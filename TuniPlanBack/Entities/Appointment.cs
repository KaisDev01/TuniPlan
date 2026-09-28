using Entities.Base;
using Entities.Enums;

namespace Entities;

public class Appointment : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid ServiceId { get; set; }
    public Service Service { get; set; } = default!;
    public Guid? ResourceId { get; set; }
    public Resource? Resource { get; set; }

    /// <summary>Null for walk-in / phone bookings without an account.</summary>
    public Guid? ClientUserId { get; set; }
    public User? ClientUser { get; set; }
    public Guid? FamilyMemberId { get; set; }
    public FamilyMember? FamilyMember { get; set; }
    public Guid? OrganizationClientId { get; set; }
    public OrganizationClient? OrganizationClient { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    /// <summary>Queue mode: ticket number of the day.</summary>
    public int? QueueNumber { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public AppointmentSource Source { get; set; } = AppointmentSource.App;

    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? ClientNote { get; set; }
    public string? BusinessNote { get; set; }
    public string? CancelReason { get; set; }
    public string? AiSummary { get; set; }
    /// <summary>Business voice note (POST .../voice-note): speech-to-text result.</summary>
    public string? VoiceNoteTranscript { get; set; }
    /// <summary>Short summary of the voice note (LLM, or the beginning of the transcript without an LLM).</summary>
    public string? VoiceNoteSummary { get; set; }

    // Counter-offer from the business
    public DateTime? ProposedStartUtc { get; set; }
    public DateTime? ProposedEndUtc { get; set; }
    public string? ProposalMessage { get; set; }

    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public decimal DepositAmount { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.NotRequired;

    public DateTime? Reminder24hSentAt { get; set; }
    public DateTime? Reminder2hSentAt { get; set; }
    public DateTime? ReviewRequestSentAt { get; set; }

    /// <summary>Optimistic concurrency (SQL Server rowversion).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Review? Review { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public bool IsActiveBooking =>
        Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed or AppointmentStatus.CounterProposed;
}

public class Review : BaseEntity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = default!;
    public Guid ClientUserId { get; set; }
    public User ClientUser { get; set; } = default!;

    public int Rating { get; set; }
    public int? RatingWelcome { get; set; }
    public int? RatingPunctuality { get; set; }
    public int? RatingQuality { get; set; }
    public int? RatingValue { get; set; }
    public string Comment { get; set; } = default!;
    public List<string> PhotoUrls { get; set; } = new();

    public string? OwnerReply { get; set; }
    public DateTime? OwnerReplyAt { get; set; }

    public bool IsReported { get; set; }
    public string? ReportReason { get; set; }
    public bool IsHidden { get; set; }

    /// <summary>Shown as "Client anonyme" everywhere, including to the business.</summary>
    public bool IsAnonymous { get; set; }
    /// <summary>Last change made by the author (UpdatedAt also changes on owner reply or report).</summary>
    public DateTime? EditedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Payment : BaseEntity
{
    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TND";
    public PaymentProvider Provider { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? ProviderReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
}

public class WaitlistEntry : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid ServiceId { get; set; }
    public Service Service { get; set; } = default!;
    public DateOnly Date { get; set; }
    public DateTime? NotifiedAt { get; set; }
}

public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;
    public Guid? AppointmentId { get; set; }
    public Guid? OrganizationId { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class AiConversation : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public Guid? DraftAppointmentId { get; set; }
    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
}

public class AiMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public AiConversation Conversation { get; set; } = default!;
    public AiMessageRole Role { get; set; }
    public string Content { get; set; } = default!;
}
