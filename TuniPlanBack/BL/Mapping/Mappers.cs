using DTOs.Account;
using DTOs.Appointments;
using DTOs.Business;
using DTOs.Catalog;
using DTOs.Organizations;
using DTOs.Reviews;
using BL.Interfaces;
using Entities;
using Entities.Enums;

namespace BL.Mapping;

public static class Mappers
{
    public static string CategoryLabel(BusinessCategory c) => c switch
    {
        BusinessCategory.Health => "Santé",
        BusinessCategory.Beauty => "Beauté",
        BusinessCategory.Home => "Maison",
        BusinessCategory.Sport => "Sport",
        BusinessCategory.Auto => "Auto",
        BusinessCategory.Pets => "Animaux",
        BusinessCategory.Legal => "Juridique",
        BusinessCategory.Creative => "Créatif",
        BusinessCategory.Education => "Éducation",
        _ => "Autre"
    };

    public static UserProfileDto ToProfile(this User u, IEnumerable<OrganizationMember>? memberships = null) => new()
    {
        Id = u.Id,
        FirstName = u.FirstName,
        LastName = u.LastName,
        Email = u.Email,
        PhoneNumber = u.PhoneNumber,
        PhoneConfirmed = u.PhoneConfirmed,
        Roles = Roles.From(u.Roles).ToList(),
        PreferredLanguage = u.PreferredLanguage,
        AvatarUrl = u.AvatarUrl,
        TwoFactorEnabled = u.TwoFactorEnabled,
        Organizations = (memberships ?? u.Memberships)
            .Where(m => m.Organization is not null)
            .Select(m => new MembershipDto(m.OrganizationId, m.Organization.Name, m.Organization.Slug, m.Role.ToString()))
            .ToList()
    };

    public static NotificationSettingsDto ToDto(this NotificationSettings s) => new()
    {
        PushEnabled = s.PushEnabled, SmsEnabled = s.SmsEnabled, WhatsAppEnabled = s.WhatsAppEnabled,
        EmailEnabled = s.EmailEnabled, Reminder24h = s.Reminder24h, Reminder2h = s.Reminder2h
    };

    public static FamilyMemberDto ToDto(this FamilyMember f) => new(f.Id, f.FirstName, f.LastName, f.Relation, f.PhoneNumber);

    public static ResourceDto ToDto(this Resource r) => new(r.Id, r.Name, r.Title, r.PhotoUrl, r.Type, r.IsActive);

    public static ServiceDto ToDto(this Service s, decimal? discountedPrice = null) => new()
    {
        Id = s.Id, Name = s.Name, Description = s.Description, Category = s.Category,
        DurationMinutes = s.DurationMinutes, Price = s.Price, DiscountedPrice = discountedPrice,
        RequiresDeposit = s.RequiresDeposit, IsActive = s.IsActive,
        ResourceIds = s.ServiceResources.Select(sr => sr.ResourceId).ToList()
    };

    public static PromotionDto ToDto(this Promotion p) => new()
    {
        Id = p.Id, Title = p.Title, Description = p.Description, DiscountPercent = p.DiscountPercent,
        ServiceId = p.ServiceId, DaysOfWeekMask = p.DaysOfWeekMask, StartTime = p.StartTime, EndTime = p.EndTime,
        ValidFrom = p.ValidFrom, ValidTo = p.ValidTo, IsLastMinute = p.IsLastMinute, IsActive = p.IsActive
    };

    public static OpeningHourDto ToDto(this OpeningHour h) => new()
    {
        DayOfWeek = h.DayOfWeek, IsClosed = h.IsClosed, OpenTime = h.OpenTime, CloseTime = h.CloseTime,
        BreakStart = h.BreakStart, BreakEnd = h.BreakEnd
    };

    public static BookingRulesDto ToRules(this Organization o) => new()
    {
        BookingType = o.BookingType, ExternalBookingEnabled = o.ExternalBookingEnabled,
        ManualValidationRequired = o.ManualValidationRequired, CancellationDeadlineHours = o.CancellationDeadlineHours,
        SlotStepMinutes = o.SlotStepMinutes, DepositMode = o.DepositMode, DepositValue = o.DepositValue,
        AutoRemindersEnabled = o.AutoRemindersEnabled, ReminderTemplate = o.ReminderTemplate
    };

    public static ClosedPeriodDto ToDto(this ClosedPeriod c) => new(c.Id, c.ResourceId, c.StartUtc, c.EndUtc, c.Reason);

    public static string PublicName(User u) =>
        string.IsNullOrWhiteSpace(u.LastName) ? u.FirstName : $"{u.FirstName} {char.ToUpperInvariant(u.LastName[0])}.";

    public static ReviewDto ToDto(this Review r) => new()
    {
        Id = r.Id,
        ClientName = r.ClientUser is null ? "Client" : PublicName(r.ClientUser),
        Rating = r.Rating, RatingWelcome = r.RatingWelcome, RatingPunctuality = r.RatingPunctuality,
        RatingQuality = r.RatingQuality, RatingValue = r.RatingValue, Comment = r.Comment,
        PhotoUrls = r.PhotoUrls, ServiceName = r.Appointment?.Service?.Name, CreatedAt = r.CreatedAt,
        OwnerReply = r.OwnerReply, OwnerReplyAt = r.OwnerReplyAt
    };

    public static NotificationDto ToDto(this Notification n) =>
        new(n.Id, n.Type, n.Title, n.Body, n.AppointmentId, n.OrganizationId, n.CreatedAt, n.ReadAt is not null);

    /// <summary>Requires Organization, Service, Resource, ClientUser, FamilyMember, OrganizationClient, Review to be loaded.</summary>
    public static AppointmentDto ToDto(this Appointment a, DateTime utcNow, bool forBusiness)
    {
        var deadline = a.Organization?.CancellationDeadlineHours ?? 24;
        var beforeDeadline = a.Status == AppointmentStatus.Pending || a.StartUtc - utcNow >= TimeSpan.FromHours(deadline);
        var clientName = a.ClientUser is not null
            ? (forBusiness ? a.ClientUser.FullName : a.ClientUser.FirstName)
            : a.OrganizationClient?.DisplayName ?? "Client";
        return new AppointmentDto
        {
            Id = a.Id,
            OrganizationId = a.OrganizationId,
            OrganizationName = a.Organization?.Name ?? "",
            OrganizationSlug = a.Organization?.Slug ?? "",
            OrganizationAddress = a.Organization?.Address,
            ServiceId = a.ServiceId,
            ServiceName = a.Service?.Name ?? "",
            DurationMinutes = (int)(a.EndUtc - a.StartUtc).TotalMinutes,
            ResourceId = a.ResourceId,
            ResourceName = a.Resource?.Name,
            ClientName = clientName,
            ClientPhone = forBusiness ? a.ClientUser?.PhoneNumber ?? a.OrganizationClient?.PhoneNumber : null,
            ForFamilyMember = a.FamilyMember is null ? null : $"{a.FamilyMember.FirstName} ({a.FamilyMember.Relation})",
            StartUtc = a.StartUtc,
            EndUtc = a.EndUtc,
            QueueNumber = a.QueueNumber,
            Status = a.Status,
            Source = a.Source,
            Price = a.Price,
            DepositAmount = a.DepositAmount,
            PaymentStatus = a.PaymentStatus,
            ClientNote = a.ClientNote,
            BusinessNote = forBusiness ? a.BusinessNote : null,
            AiSummary = forBusiness ? a.AiSummary : null,
            CancelReason = a.CancelReason,
            ProposedStartUtc = a.ProposedStartUtc,
            ProposedEndUtc = a.ProposedEndUtc,
            ProposalMessage = a.ProposalMessage,
            CanCancel = !forBusiness && a.IsActiveBooking && a.StartUtc > utcNow && beforeDeadline,
            CanReschedule = !forBusiness && a.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed
                            && a.StartUtc > utcNow && beforeDeadline,
            CanReview = !forBusiness && a.Status == AppointmentStatus.Completed && a.Review is null
                        && a.ClientUserId is not null && (utcNow - a.StartUtc).TotalDays <= 60,
            HasReview = a.Review is not null,
            CreatedAt = a.CreatedAt
        };
    }
}
