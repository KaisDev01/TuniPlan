using System.ComponentModel.DataAnnotations;
using Entities.Enums;

namespace DTOs.Appointments;

public sealed record AppointmentDto
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string OrganizationName { get; init; } = default!;
    public string OrganizationSlug { get; init; } = default!;
    public string? OrganizationAddress { get; init; }
    public Guid ServiceId { get; init; }
    public string ServiceName { get; init; } = default!;
    public int DurationMinutes { get; init; }
    public Guid? ResourceId { get; init; }
    public string? ResourceName { get; init; }
    public string ClientName { get; init; } = default!;
    public string? ClientPhone { get; init; }
    public string? ForFamilyMember { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public int? QueueNumber { get; init; }
    public AppointmentStatus Status { get; init; }
    public AppointmentSource Source { get; init; }
    public decimal Price { get; init; }
    public decimal DepositAmount { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public string? ClientNote { get; init; }
    /// <summary>Only filled for the business.</summary>
    public string? BusinessNote { get; init; }
    public string? AiSummary { get; init; }
    public string? CancelReason { get; init; }
    public DateTime? ProposedStartUtc { get; init; }
    public DateTime? ProposedEndUtc { get; init; }
    public string? ProposalMessage { get; init; }
    public bool CanCancel { get; init; }
    public bool CanReschedule { get; init; }
    public bool CanReview { get; init; }
    public bool HasReview { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record BookAppointmentRequest
{
    [Required] public Guid OrganizationId { get; init; }
    [Required] public Guid ServiceId { get; init; }
    public Guid? ResourceId { get; init; }
    /// <summary>Slot mode: start of the chosen slot (UTC).</summary>
    public DateTime? StartUtc { get; init; }
    /// <summary>Rental mode: first and last day.</summary>
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public Guid? FamilyMemberId { get; init; }
    [StringLength(1000)] public string? ClientNote { get; init; }
}

public sealed record MyAppointmentsQuery
{
    /// <summary>upcoming | past | all</summary>
    [RegularExpression("^(upcoming|past|all)$")] public string Scope { get; init; } = "upcoming";
}

public sealed record CancelAppointmentRequest
{
    [StringLength(500)] public string? Reason { get; init; }
}

public sealed record RescheduleRequest
{
    [Required] public DateTime StartUtc { get; init; }
    public Guid? ResourceId { get; init; }
}

public sealed record CounterOfferRequest
{
    [Required] public DateTime ProposedStartUtc { get; init; }
    public Guid? ResourceId { get; init; }
    [StringLength(500)] public string? Message { get; init; }
}

public sealed record RespondCounterOfferRequest
{
    public bool Accept { get; init; }
}

public sealed record RefuseRequest
{
    [StringLength(500)] public string? Reason { get; init; }
}

public sealed record WalkInRequest
{
    [Required] public Guid ServiceId { get; init; }
    public Guid? ResourceId { get; init; }
    [Required] public DateTime StartUtc { get; init; }
    public Guid? OrganizationClientId { get; init; }
    [StringLength(160)] public string? ClientName { get; init; }
    [StringLength(20)] public string? ClientPhone { get; init; }
    [StringLength(1000)] public string? Note { get; init; }
    public AppointmentSource Source { get; init; } = AppointmentSource.WalkIn;
}

public sealed record AgendaQuery
{
    [Required] public DateOnly From { get; init; }
    [Required] public DateOnly To { get; init; }
    public Guid? ResourceId { get; init; }
    public AppointmentStatus? Status { get; init; }
}

public sealed record BusinessNoteRequest
{
    [StringLength(2000)] public string? Note { get; init; }
}

public sealed record JoinWaitlistRequest
{
    [Required] public Guid OrganizationId { get; init; }
    [Required] public Guid ServiceId { get; init; }
    [Required] public DateOnly Date { get; init; }
}

public sealed record WaitlistEntryDto(Guid Id, Guid OrganizationId, string OrganizationName, Guid ServiceId, string ServiceName, DateOnly Date, bool Notified);
