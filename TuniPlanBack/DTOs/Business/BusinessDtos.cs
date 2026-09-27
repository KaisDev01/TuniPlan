using System.ComponentModel.DataAnnotations;
using DTOs.Appointments;
using DTOs.Organizations;
using Entities.Enums;

namespace DTOs.Business;

public sealed record WeekdayCountDto(DayOfWeek Day, int Count);

public sealed record DashboardDto
{
    public int TodayCount { get; init; }
    public int PendingRequests { get; init; }
    public AppointmentDto? NextAppointment { get; init; }
    public decimal WeekRevenue { get; init; }
    public int WeekAppointments { get; init; }
    public double FillRatePercent { get; init; }
    public double NoShowRatePercent { get; init; }
    public int NewClients { get; init; }
    public double RatingAverage { get; init; }
    public int ReviewCount { get; init; }
    public IReadOnlyList<WeekdayCountDto> PerWeekday { get; init; } = [];
    public ProfileCompletionDto Completion { get; init; } = default!;
}

public sealed record ClientSummaryDto
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public string DisplayName { get; init; } = default!;
    public string? PhoneNumber { get; init; }
    public int TotalVisits { get; init; }
    public DateTime? LastVisitUtc { get; init; }
    public int NoShowCount { get; init; }
    public decimal TotalSpent { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public bool IsBlocked { get; init; }
}

public sealed record ClientDetailsDto
{
    public ClientSummaryDto Summary { get; init; } = default!;
    public string? PrivateNotes { get; init; }
    public IReadOnlyList<AppointmentDto> History { get; init; } = [];
}

public sealed record CreateClientRequest
{
    [Required, StringLength(160, MinimumLength = 2)] public string DisplayName { get; init; } = default!;
    [StringLength(20)] public string? PhoneNumber { get; init; }
}

public sealed record UpdateClientRequest
{
    [StringLength(2000)] public string? PrivateNotes { get; init; }
    [MaxLength(10)] public IReadOnlyList<string> Tags { get; init; } = [];
    public bool IsBlocked { get; init; }
}

public sealed record NotificationDto(Guid Id, NotificationType Type, string Title, string Body, Guid? AppointmentId,
    Guid? OrganizationId, DateTime CreatedAt, bool IsRead);

public sealed record InitiateDepositRequest
{
    [Required] public Guid AppointmentId { get; init; }
    public PaymentProvider Provider { get; init; } = PaymentProvider.Mock;
}

public sealed record PaymentDto(Guid Id, Guid AppointmentId, decimal Amount, string Currency, PaymentProvider Provider,
    PaymentStatus Status, string? ProviderReference, string? CheckoutUrl);

public sealed record ConfirmMockPaymentRequest
{
    [Required] public Guid PaymentId { get; init; }
    public bool Success { get; init; } = true;
}

public sealed record AiChatRequest
{
    [Required] public Guid OrganizationId { get; init; }
    public Guid? ConversationId { get; init; }
    [Required, StringLength(1000, MinimumLength = 1)] public string Message { get; init; } = default!;
}

public sealed record AiChatResponse(Guid ConversationId, string Reply, IReadOnlyList<Catalog.SlotDto> ProposedSlots, Guid? SuggestedServiceId);

public sealed record AiCreateRequestRequest
{
    [Required] public Guid ConversationId { get; init; }
    [Required] public Guid ServiceId { get; init; }
    [Required] public DateTime StartUtc { get; init; }
    public Guid? ResourceId { get; init; }
}
