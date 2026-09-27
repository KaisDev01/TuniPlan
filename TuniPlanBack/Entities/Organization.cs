using Entities.Base;
using Entities.Enums;

namespace Entities;

public class Organization : BaseEntity, ISoftDelete
{
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public BusinessCategory Category { get; set; }
    public string? Subcategory { get; set; }
    public string? Description { get; set; }

    public string Governorate { get; set; } = default!;
    public string City { get; set; } = default!;
    public string Address { get; set; } = default!;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string TimeZoneId { get; set; } = "Africa/Tunis";

    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }

    // Booking rules
    public BookingType BookingType { get; set; } = BookingType.Slot;
    public bool ExternalBookingEnabled { get; set; } = true;
    public bool ManualValidationRequired { get; set; } = true;
    public int CancellationDeadlineHours { get; set; } = 24;
    public int SlotStepMinutes { get; set; } = 15;
    public DepositMode DepositMode { get; set; } = DepositMode.None;
    public decimal DepositValue { get; set; }

    // Reminders
    public bool AutoRemindersEnabled { get; set; } = true;
    public string ReminderTemplate { get; set; } =
        "Bonjour {prenom}, rappel de votre RDV chez {entreprise} le {date} à {heure}.";

    // Verification & publication
    public string? TaxId { get; set; }
    public string? RneNumber { get; set; }
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NotSubmitted;
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }

    // Denormalized for fast listing
    public double RatingAverage { get; set; }
    public int ReviewCount { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
    public ICollection<OrganizationPhoto> Photos { get; set; } = new List<OrganizationPhoto>();
    public ICollection<OpeningHour> OpeningHours { get; set; } = new List<OpeningHour>();
    public ICollection<ClosedPeriod> ClosedPeriods { get; set; } = new List<ClosedPeriod>();
    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
    public ICollection<Service> Services { get; set; } = new List<Service>();
    public ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();
}

public class OrganizationMember : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public MemberRole Role { get; set; } = MemberRole.Owner;
}

public class OrganizationPhoto : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public string Url { get; set; } = default!;
    public int SortOrder { get; set; }
}

public class OpeningHour : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public TimeOnly? BreakStart { get; set; }
    public TimeOnly? BreakEnd { get; set; }
}

/// <summary>Holidays, days off, blocked time. ResourceId null = whole organization.</summary>
public class ClosedPeriod : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid? ResourceId { get; set; }
    public Resource? Resource { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? Reason { get; set; }
}

/// <summary>A staff member, room, vehicle or equipment that can be booked.</summary>
public class Resource : BaseEntity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Title { get; set; }
    public string? PhotoUrl { get; set; }
    public ResourceType Type { get; set; } = ResourceType.Person;
    public bool IsActive { get; set; } = true;
    public Guid? UserId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<ServiceResource> ServiceResources { get; set; } = new List<ServiceResource>();
}

public class Service : BaseEntity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public decimal Price { get; set; }
    public bool RequiresDeposit { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<ServiceResource> ServiceResources { get; set; } = new List<ServiceResource>();
}

public class ServiceResource
{
    public Guid ServiceId { get; set; }
    public Service Service { get; set; } = default!;
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = default!;
}

public class Promotion : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid? ServiceId { get; set; }
    public Service? Service { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public int DiscountPercent { get; set; }
    /// <summary>Bit mask: bit 0 = Sunday ... bit 6 = Saturday. 0 = every day.</summary>
    public int DaysOfWeekMask { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsLastMinute { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>CRM record: what a business knows about one of its clients.</summary>
public class OrganizationClient : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = default!;
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string DisplayName { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public string? PrivateNotes { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsBlocked { get; set; }
}
