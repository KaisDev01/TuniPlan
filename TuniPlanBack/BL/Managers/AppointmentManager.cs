using System.Data;
using System.Globalization;
using BL.Interfaces;
using BL.Mapping;
using BL.Options;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Appointments;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BL.Managers;

public interface IAppointmentManager
{
    // Client
    Task<AppointmentDto> BookAsync(BookAppointmentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentDto>> GetMineAsync(MyAppointmentsQuery query, CancellationToken ct = default);
    Task<AppointmentDto> GetMineByIdAsync(Guid id, CancellationToken ct = default);
    Task<AppointmentDto> CancelByClientAsync(Guid id, CancelAppointmentRequest request, CancellationToken ct = default);
    Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct = default);
    Task<AppointmentDto> RespondCounterOfferAsync(Guid id, RespondCounterOfferRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentDto>> GetToReviewAsync(CancellationToken ct = default);
    Task<string> GetIcsAsync(Guid id, CancellationToken ct = default);

    // Waiting list
    Task<WaitlistEntryDto> JoinWaitlistAsync(JoinWaitlistRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WaitlistEntryDto>> GetMyWaitlistAsync(CancellationToken ct = default);
    Task LeaveWaitlistAsync(Guid id, CancellationToken ct = default);

    // Business
    Task<IReadOnlyList<AppointmentDto>> GetAgendaAsync(Guid organizationId, AgendaQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<AppointmentDto>> GetPendingRequestsAsync(Guid organizationId, CancellationToken ct = default);
    Task<AppointmentDto> GetForBusinessAsync(Guid organizationId, Guid id, CancellationToken ct = default);
    Task<AppointmentDto> ConfirmAsync(Guid organizationId, Guid id, CancellationToken ct = default);
    Task<AppointmentDto> RefuseAsync(Guid organizationId, Guid id, RefuseRequest request, CancellationToken ct = default);
    Task<AppointmentDto> CounterOfferAsync(Guid organizationId, Guid id, CounterOfferRequest request, CancellationToken ct = default);
    Task<AppointmentDto> CompleteAsync(Guid organizationId, Guid id, CancellationToken ct = default);
    Task<AppointmentDto> MarkNoShowAsync(Guid organizationId, Guid id, CancellationToken ct = default);
    Task<AppointmentDto> CancelByBusinessAsync(Guid organizationId, Guid id, CancelAppointmentRequest request, CancellationToken ct = default);
    Task<AppointmentDto> RescheduleByBusinessAsync(Guid organizationId, Guid id, RescheduleRequest request, CancellationToken ct = default);
    Task<AppointmentDto> CreateWalkInAsync(Guid organizationId, WalkInRequest request, CancellationToken ct = default);
    Task<AppointmentDto> UpdateBusinessNoteAsync(Guid organizationId, Guid id, BusinessNoteRequest request, CancellationToken ct = default);

    /// <summary>Used by the AI secretary: always creates a PENDING request (the owner must confirm).</summary>
    Task<AppointmentDto> BookFromAiAsync(BookAppointmentRequest request, string aiSummary, CancellationToken ct = default);
}

public sealed class AppointmentManager(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IOrganizationAccess access,
    IAvailabilityManager availability,
    IClientManager clients,
    INotificationManager notifications,
    IPaymentManager payments,
    IClock clock,
    IOptions<AppOptions> appOptions) : IAppointmentManager
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ================================================================= Client booking
    public Task<AppointmentDto> BookAsync(BookAppointmentRequest request, CancellationToken ct = default) =>
        BookInternalAsync(request, AppointmentSource.App, forcePending: false, aiSummary: null, ct);

    public Task<AppointmentDto> BookFromAiAsync(BookAppointmentRequest request, string aiSummary, CancellationToken ct = default) =>
        BookInternalAsync(request, AppointmentSource.AiSecretary, forcePending: true, aiSummary, ct);

    private async Task<AppointmentDto> BookInternalAsync(BookAppointmentRequest request, AppointmentSource source, bool forcePending,
        string? aiSummary, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var user = await uow.Users.GetByIdAsync(userId, ct) ?? throw new UnauthorizedException();
        if (!user.PhoneConfirmed) throw new ForbiddenException("Vérifiez votre numéro de téléphone avant de réserver.", "phone_not_verified");

        var org = await uow.Organizations.QueryNoTracking()
                      .Include(o => o.OpeningHours).Include(o => o.Promotions.Where(p => p.IsActive))
                      .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, ct)
                  ?? throw new NotFoundException("Entreprise introuvable.");
        if (!org.IsPublished || !org.ExternalBookingEnabled)
            throw new BusinessRuleException("Cette entreprise n'accepte pas de réservations en ligne pour le moment.");

        var service = await uow.Services.QueryNoTracking().Include(s => s.ServiceResources)
                          .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.OrganizationId == org.Id && s.IsActive, ct)
                      ?? throw new NotFoundException("Prestation introuvable.");

        if (request.FamilyMemberId is { } fid && !await uow.FamilyMembers.AnyAsync(f => f.Id == fid && f.UserId == userId, ct))
            throw ValidationException.For(nameof(request.FamilyMemberId), "Proche inconnu.");

        var crm = await uow.OrganizationClients.FirstOrDefaultAsync(c => c.OrganizationId == org.Id && c.UserId == userId, ct);
        if (crm?.IsBlocked == true) throw new ForbiddenException("Vous ne pouvez plus réserver en ligne chez cet établissement. Contactez-le directement.", "client_blocked");

        var pendingCount = await uow.Appointments.CountAsync(a => a.OrganizationId == org.Id && a.ClientUserId == userId
                                                                  && a.Status == AppointmentStatus.Pending, ct);
        if (pendingCount >= appOptions.Value.MaxPendingRequestsPerClientPerOrganization)
            throw new BusinessRuleException("Vous avez déjà plusieurs demandes en attente chez cet établissement.");

        var now = clock.UtcNow;
        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        DateTime start, end;
        Guid? resourceId;
        int? queueNumber = null;
        var price = service.Price;

        switch (org.BookingType)
        {
            case BookingType.Rental:
            {
                if (request.StartDate is not { } sd || request.EndDate is not { } ed || ed < sd)
                    throw ValidationException.For(nameof(request.StartDate), "Choisissez une date de début et de fin.");
                var daysCount = ed.DayNumber - sd.DayNumber + 1;
                if (daysCount > 60) throw ValidationException.For(nameof(request.EndDate), "Location limitée à 60 jours.");
                start = TimeZoneHelper.ToUtc(sd, TimeOnly.MinValue, tz);
                end = TimeZoneHelper.ToUtc(ed.AddDays(1), TimeOnly.MinValue, tz);
                if (start.AddDays(1) < now) throw ValidationException.For(nameof(request.StartDate), "La date est passée.");
                resourceId = request.ResourceId ?? throw ValidationException.For(nameof(request.ResourceId), "Choisissez un véhicule.");
                if (service.ServiceResources.Count > 0 && service.ServiceResources.All(sr => sr.ResourceId != resourceId))
                    throw ValidationException.For(nameof(request.ResourceId), "Véhicule non disponible pour cette formule.");
                price = service.Price * daysCount;
                break;
            }
            case BookingType.Queue:
            {
                if (!Availability.SlotCalculator.IsOpenAt(now, tz, org.OpeningHours))
                    throw new BusinessRuleException("L'établissement est fermé : le ticket n'est disponible que pendant les horaires d'ouverture.");
                var localToday = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(now, tz));
                var dayStart = TimeZoneHelper.ToUtc(localToday, TimeOnly.MinValue, tz);
                if (await uow.Appointments.AnyAsync(a => a.OrganizationId == org.Id && a.ClientUserId == userId && a.StartUtc >= dayStart
                                                         && a.QueueNumber != null && (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending), ct))
                    throw new ConflictException("Vous avez déjà un ticket aujourd'hui.");
                queueNumber = await uow.Appointments.CountAsync(a => a.OrganizationId == org.Id && a.StartUtc >= dayStart && a.QueueNumber != null, ct) + 1;
                start = now;
                end = now.AddMinutes(service.DurationMinutes);
                resourceId = null;
                break;
            }
            default:
            {
                if (request.StartUtc is not { } requested) throw ValidationException.For(nameof(request.StartUtc), "Choisissez un créneau.");
                start = DateTime.SpecifyKind(requested, DateTimeKind.Utc);
                end = start.AddMinutes(service.DurationMinutes);
                if (start <= now) throw ValidationException.For(nameof(request.StartUtc), "Ce créneau est déjà passé.");
                if (start > now.AddDays(appOptions.Value.BookingHorizonDays)) throw ValidationException.For(nameof(request.StartUtc), "Date trop lointaine.");
                var free = await availability.CheckSlotAsync(org, service, start, request.ResourceId, null, ct)
                           ?? throw new ConflictException("Ce créneau n'est plus disponible. Choisissez-en un autre.", "slot_unavailable");
                resourceId = request.ResourceId ?? (free.Count > 0 ? free[0] : (Guid?)null);
                break;
            }
        }

        var discount = PromotionRules.BestDiscount(org.Promotions, service.Id, TimeZoneHelper.ToLocal(start, tz), now, start);
        var finalPrice = decimal.Round(price * (100 - discount) / 100m, 3);
        var deposit = ComputeDeposit(org, service, finalPrice);

        var appointment = await uow.ExecuteInTransactionAsync(async token =>
        {
            // Re-check inside a serializable transaction: two clients cannot take the same slot.
            if (org.BookingType != BookingType.Queue
                && await uow.Appointments.HasOverlapAsync(org.Id, resourceId, start, end, null, token))
                throw new ConflictException("Ce créneau vient d'être réservé. Choisissez-en un autre.", "slot_unavailable");

            var client = crm ?? await clients.EnsureClientAsync(org.Id, user, null, null, token);
            var autoConfirm = !forcePending && (!org.ManualValidationRequired || org.BookingType == BookingType.Queue);
            var a = new Appointment
            {
                OrganizationId = org.Id, ServiceId = service.Id, ResourceId = resourceId,
                ClientUserId = userId, FamilyMemberId = request.FamilyMemberId, OrganizationClientId = client.Id,
                StartUtc = start, EndUtc = end, QueueNumber = queueNumber,
                Status = autoConfirm ? AppointmentStatus.Confirmed : AppointmentStatus.Pending,
                ConfirmedAt = autoConfirm ? now : null,
                Source = source, Price = finalPrice, DiscountPercent = discount,
                DepositAmount = deposit, PaymentStatus = deposit > 0 ? PaymentStatus.Pending : PaymentStatus.NotRequired,
                ClientNote = request.ClientNote?.Trim(), AiSummary = aiSummary
            };
            await uow.Appointments.AddAsync(a, token);

            var when = FormatLocal(start, tz, org.BookingType);
            await notifications.NotifyOrganizationAsync(org.Id, NotificationType.NewRequest,
                autoConfirm ? "Nouveau rendez-vous" : (source == AppointmentSource.AiSecretary ? "Nouvelle demande (secrétaire IA)" : "Nouvelle demande"),
                $"{user.FullName} · {service.Name} · {when}", a.Id, token);
            if (autoConfirm)
                await notifications.NotifyAsync(userId, NotificationType.RequestAccepted, "Rendez-vous confirmé",
                    queueNumber is { } n ? $"{org.Name} : votre ticket n°{n}." : $"{org.Name} · {service.Name} · {when}", a.Id, org.Id, token);
            return a;
        }, IsolationLevel.Serializable, ct);

        return await LoadDtoAsync(appointment.Id, forBusiness: false, ct);
    }

    private static decimal ComputeDeposit(Organization org, Service service, decimal price)
    {
        if (org.DepositMode == DepositMode.None) return 0;
        if (!service.RequiresDeposit && org.BookingType != BookingType.Rental) return 0;
        var amount = org.DepositMode == DepositMode.Fixed ? org.DepositValue : price * org.DepositValue / 100m;
        return decimal.Round(Math.Min(amount, price), 3);
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetMineAsync(MyAppointmentsQuery query, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var q = uow.Appointments.QueryWithDetails().AsNoTracking().Where(a => a.ClientUserId == userId);
        q = query.Scope switch
        {
            "upcoming" => q.Where(a => a.EndUtc >= now && (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed
                                                          || a.Status == AppointmentStatus.CounterProposed)).OrderBy(a => a.StartUtc),
            "past" => q.Where(a => a.EndUtc < now || !(a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed
                                                       || a.Status == AppointmentStatus.CounterProposed)).OrderByDescending(a => a.StartUtc),
            _ => q.OrderByDescending(a => a.StartUtc)
        };
        var list = await q.Take(200).ToListAsync(ct);
        return list.Select(a => a.ToDto(now, forBusiness: false)).ToList();
    }

    public async Task<AppointmentDto> GetMineByIdAsync(Guid id, CancellationToken ct = default)
    {
        var a = await GetOwnedByClientAsync(id, ct);
        return a.ToDto(clock.UtcNow, forBusiness: false);
    }

    public async Task<AppointmentDto> CancelByClientAsync(Guid id, CancelAppointmentRequest request, CancellationToken ct = default)
    {
        var a = await GetOwnedByClientAsync(id, ct);
        var now = clock.UtcNow;
        if (!a.IsActiveBooking) throw new BusinessRuleException("Ce rendez-vous ne peut plus être annulé.");
        if (a.Status != AppointmentStatus.Pending && a.StartUtc - now < TimeSpan.FromHours(a.Organization.CancellationDeadlineHours))
            throw new BusinessRuleException($"Annulation impossible moins de {a.Organization.CancellationDeadlineHours} h avant le rendez-vous. Contactez l'établissement.", "cancellation_deadline");

        a.Status = AppointmentStatus.CancelledByClient;
        a.CancelledAt = now;
        a.CancelReason = request.Reason?.Trim();
        if (a.PaymentStatus == PaymentStatus.Paid) await payments.RefundAsync(a, ct);

        await notifications.NotifyOrganizationAsync(a.OrganizationId, NotificationType.Cancellation, "Rendez-vous annulé",
            $"{a.ClientUser?.FullName} a annulé : {a.Service.Name} · {FormatLocal(a)}", a.Id, ct);
        await NotifyWaitlistAsync(a, ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(now, forBusiness: false);
    }

    public async Task<AppointmentDto> RescheduleAsync(Guid id, RescheduleRequest request, CancellationToken ct = default)
    {
        var a = await GetOwnedByClientAsync(id, ct);
        var now = clock.UtcNow;
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            throw new BusinessRuleException("Ce rendez-vous ne peut pas être déplacé.");
        if (a.Status == AppointmentStatus.Confirmed && a.StartUtc - now < TimeSpan.FromHours(a.Organization.CancellationDeadlineHours))
            throw new BusinessRuleException($"Modification impossible moins de {a.Organization.CancellationDeadlineHours} h avant le rendez-vous.", "cancellation_deadline");
        if (a.Organization.BookingType != BookingType.Slot) throw new BusinessRuleException("Ce type de réservation ne peut pas être déplacé en ligne.");

        var oldStart = a.StartUtc;
        await MoveAsync(a, request.StartUtc, request.ResourceId ?? a.ResourceId, ct);
        if (a.Organization.ManualValidationRequired) { a.Status = AppointmentStatus.Pending; a.ConfirmedAt = null; }

        await notifications.NotifyOrganizationAsync(a.OrganizationId, NotificationType.NewRequest, "Rendez-vous déplacé",
            $"{a.ClientUser?.FullName} · {a.Service.Name} : {FormatLocal(oldStart, a)} → {FormatLocal(a)}", a.Id, ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(now, forBusiness: false);
    }

    public async Task<AppointmentDto> RespondCounterOfferAsync(Guid id, RespondCounterOfferRequest request, CancellationToken ct = default)
    {
        var a = await GetOwnedByClientAsync(id, ct);
        if (a.Status != AppointmentStatus.CounterProposed || a.ProposedStartUtc is null || a.ProposedEndUtc is null)
            throw new BusinessRuleException("Aucune proposition en attente pour ce rendez-vous.");
        var now = clock.UtcNow;
        if (request.Accept)
        {
            if (a.ProposedStartUtc <= now) throw new BusinessRuleException("Le créneau proposé est déjà passé.");
            if (await uow.Appointments.HasOverlapAsync(a.OrganizationId, a.ResourceId, a.ProposedStartUtc.Value, a.ProposedEndUtc.Value, a.Id, ct))
                throw new ConflictException("Le créneau proposé n'est plus disponible.", "slot_unavailable");
            a.StartUtc = a.ProposedStartUtc.Value;
            a.EndUtc = a.ProposedEndUtc.Value;
            a.Status = AppointmentStatus.Confirmed;
            a.ConfirmedAt = now;
        }
        else
        {
            a.Status = AppointmentStatus.CancelledByClient;
            a.CancelledAt = now;
            a.CancelReason = "Proposition refusée par le client";
        }
        a.ProposedStartUtc = null;
        a.ProposedEndUtc = null;
        await notifications.NotifyOrganizationAsync(a.OrganizationId, NotificationType.General,
            request.Accept ? "Proposition acceptée" : "Proposition refusée",
            $"{a.ClientUser?.FullName} · {a.Service.Name} · {FormatLocal(a)}", a.Id, ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(now, forBusiness: false);
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetToReviewAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var since = now.AddDays(-60);
        var list = await uow.Appointments.QueryWithDetails().AsNoTracking()
            .Where(a => a.ClientUserId == userId && a.Status == AppointmentStatus.Completed && a.Review == null && a.StartUtc >= since)
            .OrderByDescending(a => a.StartUtc).Take(20).ToListAsync(ct);
        return list.Select(a => a.ToDto(now, false)).ToList();
    }

    /// <summary>iCalendar file ("Ajouter à mon calendrier").</summary>
    public async Task<string> GetIcsAsync(Guid id, CancellationToken ct = default)
    {
        var a = await GetOwnedByClientAsync(id, ct);
        static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
        return string.Join("\r\n",
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//TuniPlan//FR", "CALSCALE:GREGORIAN", "BEGIN:VEVENT",
            $"UID:{a.Id}@tuniplan", $"DTSTAMP:{clock.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
            $"DTSTART:{a.StartUtc:yyyyMMdd'T'HHmmss'Z'}", $"DTEND:{a.EndUtc:yyyyMMdd'T'HHmmss'Z'}",
            $"SUMMARY:{Esc($"{a.Service.Name} – {a.Organization.Name}")}",
            $"LOCATION:{Esc(a.Organization.Address)}", "BEGIN:VALARM", "TRIGGER:-PT2H", "ACTION:DISPLAY",
            "DESCRIPTION:Rappel TuniPlan", "END:VALARM", "END:VEVENT", "END:VCALENDAR", "");
    }

    // ================================================================= Waiting list
    public async Task<WaitlistEntryDto> JoinWaitlistAsync(JoinWaitlistRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var service = await uow.Services.QueryNoTracking().Include(s => s.Organization)
                          .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.OrganizationId == request.OrganizationId && s.IsActive, ct)
                      ?? throw new NotFoundException("Prestation introuvable.");
        if (request.Date < DateOnly.FromDateTime(clock.UtcNow)) throw ValidationException.For(nameof(request.Date), "Date passée.");
        if (await uow.Waitlist.AnyAsync(w => w.UserId == userId && w.ServiceId == request.ServiceId && w.Date == request.Date && w.NotifiedAt == null, ct))
            throw new ConflictException("Vous êtes déjà sur la liste d'attente pour ce jour.");
        if (await uow.Waitlist.CountAsync(w => w.UserId == userId && w.NotifiedAt == null, ct) >= 10)
            throw new BusinessRuleException("Maximum 10 alertes actives.");
        var entry = new WaitlistEntry { UserId = userId, OrganizationId = request.OrganizationId, ServiceId = request.ServiceId, Date = request.Date };
        await uow.Waitlist.AddAsync(entry, ct);
        await uow.SaveChangesAsync(ct);
        return new WaitlistEntryDto(entry.Id, entry.OrganizationId, service.Organization.Name, service.Id, service.Name, entry.Date, false);
    }

    public async Task<IReadOnlyList<WaitlistEntryDto>> GetMyWaitlistAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return await uow.Waitlist.QueryNoTracking()
            .Where(w => w.UserId == userId && w.Date >= today)
            .OrderBy(w => w.Date)
            .Select(w => new WaitlistEntryDto(w.Id, w.OrganizationId, w.Organization.Name, w.ServiceId, w.Service.Name, w.Date, w.NotifiedAt != null))
            .ToListAsync(ct);
    }

    public async Task LeaveWaitlistAsync(Guid id, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var entry = await uow.Waitlist.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct) ?? throw new NotFoundException();
        uow.Waitlist.Remove(entry);
        await uow.SaveChangesAsync(ct);
    }

    private async Task NotifyWaitlistAsync(Appointment freed, CancellationToken ct)
    {
        var tz = TimeZoneHelper.Find(freed.Organization.TimeZoneId);
        var date = DateOnly.FromDateTime(TimeZoneHelper.ToLocal(freed.StartUtc, tz));
        var entries = await uow.Waitlist.Query()
            .Where(w => w.OrganizationId == freed.OrganizationId && w.ServiceId == freed.ServiceId && w.Date == date && w.NotifiedAt == null)
            .OrderBy(w => w.CreatedAt).Take(20).ToListAsync(ct);
        foreach (var w in entries)
        {
            w.NotifiedAt = clock.UtcNow;
            await notifications.NotifyAsync(w.UserId, NotificationType.WaitlistSlotFree, "Un créneau s'est libéré !",
                $"{freed.Organization.Name} · {freed.Service.Name} · {FormatLocal(freed)}. Réservez vite.", null, freed.OrganizationId, ct);
        }
    }

    // ================================================================= Business side
    public async Task<IReadOnlyList<AppointmentDto>> GetAgendaAsync(Guid organizationId, AgendaQuery query, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber > 62)
            throw ValidationException.For(nameof(query.To), "Période invalide (62 jours maximum).");
        var org = await uow.Organizations.QueryNoTracking().FirstAsync(o => o.Id == organizationId, ct);
        var tz = TimeZoneHelper.Find(org.TimeZoneId);
        var from = TimeZoneHelper.ToUtc(query.From, TimeOnly.MinValue, tz);
        var to = TimeZoneHelper.ToUtc(query.To.AddDays(1), TimeOnly.MinValue, tz);

        var q = uow.Appointments.QueryWithDetails().AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.StartUtc < to && a.EndUtc > from);
        if (query.ResourceId is { } rid) q = q.Where(a => a.ResourceId == rid);
        if (query.Status is { } st) q = q.Where(a => a.Status == st);
        else q = q.Where(a => a.Status != AppointmentStatus.Refused);
        var now = clock.UtcNow;
        return (await q.OrderBy(a => a.StartUtc).ToListAsync(ct)).Select(a => a.ToDto(now, true)).ToList();
    }

    public async Task<IReadOnlyList<AppointmentDto>> GetPendingRequestsAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var now = clock.UtcNow;
        var list = await uow.Appointments.QueryWithDetails().AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.Status == AppointmentStatus.Pending && a.StartUtc > now)
            .OrderBy(a => a.StartUtc).ToListAsync(ct);
        return list.Select(a => a.ToDto(now, true)).ToList();
    }

    public async Task<AppointmentDto> GetForBusinessAsync(Guid organizationId, Guid id, CancellationToken ct = default) =>
        (await GetForOrgAsync(organizationId, id, ct)).ToDto(clock.UtcNow, true);

    public async Task<AppointmentDto> ConfirmAsync(Guid organizationId, Guid id, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (a.Status != AppointmentStatus.Pending) throw new BusinessRuleException("Seules les demandes en attente peuvent être confirmées.");
        if (a.StartUtc <= clock.UtcNow) throw new BusinessRuleException("Cette demande est expirée.");
        a.Status = AppointmentStatus.Confirmed;
        a.ConfirmedAt = clock.UtcNow;
        await NotifyClientAsync(a, NotificationType.RequestAccepted, "Rendez-vous confirmé", $"{a.Organization.Name} · {a.Service.Name} · {FormatLocal(a)}", ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> RefuseAsync(Guid organizationId, Guid id, RefuseRequest request, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.CounterProposed))
            throw new BusinessRuleException("Cette demande ne peut plus être refusée.");
        a.Status = AppointmentStatus.Refused;
        a.CancelledAt = clock.UtcNow;
        a.CancelReason = request.Reason?.Trim();
        if (a.PaymentStatus == PaymentStatus.Paid) await payments.RefundAsync(a, ct);
        await NotifyClientAsync(a, NotificationType.RequestRefused, "Demande non acceptée",
            $"{a.Organization.Name} ne peut pas vous recevoir {FormatLocal(a)}." + (string.IsNullOrWhiteSpace(a.CancelReason) ? "" : $" Motif : {a.CancelReason}"), ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> CounterOfferAsync(Guid organizationId, Guid id, CounterOfferRequest request, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            throw new BusinessRuleException("Impossible de proposer un autre créneau pour ce rendez-vous.");
        var start = DateTime.SpecifyKind(request.ProposedStartUtc, DateTimeKind.Utc);
        if (start <= clock.UtcNow) throw ValidationException.For(nameof(request.ProposedStartUtc), "Le créneau proposé est passé.");
        var end = start + (a.EndUtc - a.StartUtc);
        var resourceId = request.ResourceId ?? a.ResourceId;
        if (await uow.Appointments.HasOverlapAsync(organizationId, resourceId, start, end, a.Id, ct))
            throw new ConflictException("Ce créneau est déjà occupé.", "slot_unavailable");
        a.ResourceId = resourceId;
        a.ProposedStartUtc = start;
        a.ProposedEndUtc = end;
        a.ProposalMessage = request.Message?.Trim();
        a.Status = AppointmentStatus.CounterProposed;
        await NotifyClientAsync(a, NotificationType.CounterOffer, "Nouvelle proposition de créneau",
            $"{a.Organization.Name} vous propose {FormatLocal(start, a)} pour {a.Service.Name}.", ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> CompleteAsync(Guid organizationId, Guid id, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (a.Status != AppointmentStatus.Confirmed) throw new BusinessRuleException("Seul un rendez-vous confirmé peut être marqué « terminé ».");
        if (a.StartUtc > clock.UtcNow) throw new BusinessRuleException("Le rendez-vous n'a pas encore commencé.");
        a.Status = AppointmentStatus.Completed;
        a.CompletedAt = clock.UtcNow;
        if (a.OrganizationClient is not null) a.OrganizationClient.Tags.Remove("nouveau");
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> MarkNoShowAsync(Guid organizationId, Guid id, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (a.Status != AppointmentStatus.Confirmed) throw new BusinessRuleException("Seul un rendez-vous confirmé peut être marqué « absent ».");
        if (a.StartUtc > clock.UtcNow) throw new BusinessRuleException("Le rendez-vous n'a pas encore commencé.");
        a.Status = AppointmentStatus.NoShow;
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> CancelByBusinessAsync(Guid organizationId, Guid id, CancelAppointmentRequest request, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (!a.IsActiveBooking) throw new BusinessRuleException("Ce rendez-vous n'est plus actif.");
        a.Status = AppointmentStatus.CancelledByBusiness;
        a.CancelledAt = clock.UtcNow;
        a.CancelReason = request.Reason?.Trim();
        if (a.PaymentStatus == PaymentStatus.Paid) await payments.RefundAsync(a, ct);
        await NotifyClientAsync(a, NotificationType.Cancellation, "Rendez-vous annulé",
            $"{a.Organization.Name} a annulé votre rendez-vous du {FormatLocal(a)}." + (string.IsNullOrWhiteSpace(a.CancelReason) ? "" : $" Motif : {a.CancelReason}"), ct, external: true);
        await NotifyWaitlistAsync(a, ct);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> RescheduleByBusinessAsync(Guid organizationId, Guid id, RescheduleRequest request, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        if (a.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
            throw new BusinessRuleException("Ce rendez-vous ne peut pas être déplacé.");
        var start = DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc);
        var end = start + (a.EndUtc - a.StartUtc);
        var resourceId = request.ResourceId ?? a.ResourceId;
        if (await uow.Appointments.HasOverlapAsync(organizationId, resourceId, start, end, a.Id, ct))
            throw new ConflictException("Ce créneau est déjà occupé.", "slot_unavailable");
        var old = a.StartUtc;
        a.StartUtc = start;
        a.EndUtc = end;
        a.ResourceId = resourceId;
        a.Reminder24hSentAt = null;
        a.Reminder2hSentAt = null;
        await NotifyClientAsync(a, NotificationType.General, "Rendez-vous déplacé",
            $"{a.Organization.Name} a déplacé votre rendez-vous : {FormatLocal(old, a)} → {FormatLocal(a)}.", ct, external: true);
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    public async Task<AppointmentDto> CreateWalkInAsync(Guid organizationId, WalkInRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var service = await uow.Services.QueryNoTracking().FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.OrganizationId == organizationId, ct)
                      ?? throw new NotFoundException("Prestation introuvable.");
        var start = DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc);
        var end = start.AddMinutes(service.DurationMinutes);
        if (request.ResourceId is { } rid && !await uow.Resources.AnyAsync(r => r.Id == rid && r.OrganizationId == organizationId, ct))
            throw ValidationException.For(nameof(request.ResourceId), "Ressource inconnue.");

        var appointment = await uow.ExecuteInTransactionAsync(async token =>
        {
            if (await uow.Appointments.HasOverlapAsync(organizationId, request.ResourceId, start, end, null, token))
                throw new ConflictException("Ce créneau est déjà occupé.", "slot_unavailable");

            OrganizationClient client;
            if (request.OrganizationClientId is { } cid)
                client = await uow.OrganizationClients.FirstOrDefaultAsync(c => c.Id == cid && c.OrganizationId == organizationId, token)
                         ?? throw ValidationException.For(nameof(request.OrganizationClientId), "Client inconnu.");
            else
            {
                if (string.IsNullOrWhiteSpace(request.ClientName) && string.IsNullOrWhiteSpace(request.ClientPhone))
                    throw ValidationException.For(nameof(request.ClientName), "Indiquez le nom ou le téléphone du client.");
                var phone = PhoneNumberHelper.NormalizeTunisian(request.ClientPhone);
                var user = phone is null ? null : await uow.Users.GetByPhoneAsync(phone, token);
                client = await clients.EnsureClientAsync(organizationId, user, request.ClientName, phone, token);
            }

            var a = new Appointment
            {
                OrganizationId = organizationId, ServiceId = service.Id, ResourceId = request.ResourceId,
                ClientUserId = client.UserId, OrganizationClientId = client.Id,
                StartUtc = start, EndUtc = end, Status = AppointmentStatus.Confirmed, ConfirmedAt = clock.UtcNow,
                Source = request.Source is AppointmentSource.Phone ? AppointmentSource.Phone : AppointmentSource.WalkIn,
                Price = service.Price, BusinessNote = request.Note?.Trim()
            };
            await uow.Appointments.AddAsync(a, token);
            return a;
        }, IsolationLevel.Serializable, ct);

        return await LoadDtoAsync(appointment.Id, forBusiness: true, ct);
    }

    public async Task<AppointmentDto> UpdateBusinessNoteAsync(Guid organizationId, Guid id, BusinessNoteRequest request, CancellationToken ct = default)
    {
        var a = await GetForOrgAsync(organizationId, id, ct);
        a.BusinessNote = request.Note?.Trim();
        await uow.SaveChangesAsync(ct);
        return a.ToDto(clock.UtcNow, true);
    }

    // ================================================================= Helpers
    private async Task<Appointment> GetOwnedByClientAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var a = await uow.Appointments.QueryWithDetails().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Rendez-vous introuvable.");
        if (a.ClientUserId != userId) throw new NotFoundException("Rendez-vous introuvable.");
        return a;
    }

    private async Task<Appointment> GetForOrgAsync(Guid organizationId, Guid id, CancellationToken ct)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        return await uow.Appointments.QueryWithDetails().FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct)
               ?? throw new NotFoundException("Rendez-vous introuvable.");
    }

    private async Task<AppointmentDto> LoadDtoAsync(Guid id, bool forBusiness, CancellationToken ct)
    {
        var a = await uow.Appointments.QueryWithDetails().AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return a.ToDto(clock.UtcNow, forBusiness);
    }

    private async Task MoveAsync(Appointment a, DateTime newStartUtc, Guid? resourceId, CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(newStartUtc, DateTimeKind.Utc);
        if (start <= clock.UtcNow) throw ValidationException.For("StartUtc", "Ce créneau est déjà passé.");
        var org = await uow.Organizations.QueryNoTracking().Include(o => o.OpeningHours).FirstAsync(o => o.Id == a.OrganizationId, ct);
        var service = await uow.Services.QueryNoTracking().Include(s => s.ServiceResources).FirstAsync(s => s.Id == a.ServiceId, ct);
        var free = await availability.CheckSlotAsync(org, service, start, resourceId, a.Id, ct)
                   ?? throw new ConflictException("Ce créneau n'est pas disponible.", "slot_unavailable");
        a.StartUtc = start;
        a.EndUtc = start.AddMinutes(service.DurationMinutes);
        a.ResourceId = resourceId ?? (free.Count > 0 ? free[0] : (Guid?)null);
        a.Reminder24hSentAt = null;
        a.Reminder2hSentAt = null;
    }

    private async Task NotifyClientAsync(Appointment a, NotificationType type, string title, string body, CancellationToken ct, bool external = false)
    {
        if (a.ClientUserId is not { } clientId) return;
        await notifications.NotifyAsync(clientId, type, title, body, a.Id, a.OrganizationId, ct);
        if (external && a.ClientUser is not null) await notifications.SendExternalAsync(a.ClientUser, $"{title} – {body}", ct);
    }

    private static string FormatLocal(Appointment a) => FormatLocal(a.StartUtc, a);

    private static string FormatLocal(DateTime utc, Appointment a) =>
        FormatLocal(utc, TimeZoneHelper.Find(a.Organization?.TimeZoneId), a.Organization?.BookingType ?? BookingType.Slot);

    internal static string FormatLocal(DateTime utc, TimeZoneInfo tz, BookingType type)
    {
        var local = TimeZoneHelper.ToLocal(utc, tz);
        return type == BookingType.Rental ? local.ToString("dddd d MMMM", Fr) : local.ToString("dddd d MMMM 'à' HH:mm", Fr);
    }
}
