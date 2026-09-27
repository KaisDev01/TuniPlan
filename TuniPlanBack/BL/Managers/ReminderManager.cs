using System.Globalization;
using Common.Helpers;
using DAO.Interfaces;
using Entities;
using Entities.Enums;
using LoggerService;
using Microsoft.EntityFrameworkCore;

namespace BL.Managers;

/// <summary>Called every few minutes by a background job: reminders, review requests, expired requests.</summary>
public interface IReminderManager
{
    Task<int> ProcessAsync(CancellationToken ct = default);
}

public sealed class ReminderManager(IUnitOfWork uow, INotificationManager notifications, ILoggerManager logger, IClock clock) : IReminderManager
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public async Task<int> ProcessAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var processed = 0;

        // 1) 24 h and 2 h reminders
        var upcoming = await uow.Appointments.Query()
            .Include(a => a.Organization).Include(a => a.Service).Include(a => a.ClientUser)
            .Where(a => a.Status == AppointmentStatus.Confirmed && a.ClientUserId != null && a.Organization.AutoRemindersEnabled
                        && a.StartUtc > now && a.StartUtc <= now.AddHours(24)
                        && (a.Reminder24hSentAt == null || a.Reminder2hSentAt == null))
            .OrderBy(a => a.StartUtc).Take(200).ToListAsync(ct);

        foreach (var a in upcoming)
        {
            var user = a.ClientUser!;
            var inTwoHours = a.StartUtc <= now.AddHours(2);
            if (inTwoHours && a.Reminder2hSentAt is null)
            {
                a.Reminder2hSentAt = now;
                a.Reminder24hSentAt ??= now;
                if (user.NotificationSettings.Reminder2h) await SendReminderAsync(a, user, ct);
                processed++;
            }
            else if (!inTwoHours && a.Reminder24hSentAt is null)
            {
                a.Reminder24hSentAt = now;
                if (user.NotificationSettings.Reminder24h) await SendReminderAsync(a, user, ct);
                processed++;
            }
        }

        // 2) Review requests (2 h after a completed appointment)
        var toReview = await uow.Appointments.Query()
            .Include(a => a.Organization)
            .Where(a => a.Status == AppointmentStatus.Completed && a.ClientUserId != null && a.ReviewRequestSentAt == null
                        && a.EndUtc <= now.AddHours(-2) && a.EndUtc >= now.AddDays(-7) && a.Review == null)
            .OrderBy(a => a.EndUtc).Take(200).ToListAsync(ct);
        foreach (var a in toReview)
        {
            a.ReviewRequestSentAt = now;
            await notifications.NotifyAsync(a.ClientUserId!.Value, NotificationType.ReviewRequest,
                $"Comment s'est passé votre RDV chez {a.Organization.Name} ?", "Laissez un avis vérifié en 30 secondes.", a.Id, a.OrganizationId, ct);
            processed++;
        }

        // 3) Requests never answered whose time has passed
        var expired = await uow.Appointments.Query()
            .Include(a => a.Organization)
            .Where(a => (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.CounterProposed) && a.StartUtc < now)
            .OrderBy(a => a.StartUtc).Take(200).ToListAsync(ct);
        foreach (var a in expired)
        {
            a.Status = AppointmentStatus.Refused;
            a.CancelReason = "Demande expirée";
            a.CancelledAt = now;
            if (a.ClientUserId is { } cid)
                await notifications.NotifyAsync(cid, NotificationType.RequestRefused, "Demande expirée",
                    $"{a.Organization.Name} n'a pas répondu à temps. Choisissez un autre créneau.", a.Id, a.OrganizationId, ct);
            processed++;
        }

        if (processed > 0)
        {
            await uow.SaveChangesAsync(ct);
            logger.LogInfo("Reminder job processed {Count} items", processed);
        }
        return processed;
    }

    private async Task SendReminderAsync(Appointment a, User user, CancellationToken ct)
    {
        var tz = TimeZoneHelper.Find(a.Organization.TimeZoneId);
        var local = TimeZoneHelper.ToLocal(a.StartUtc, tz);
        var text = a.Organization.ReminderTemplate
            .Replace("{prenom}", user.FirstName)
            .Replace("{prénom}", user.FirstName)
            .Replace("{entreprise}", a.Organization.Name)
            .Replace("{service}", a.Service.Name)
            .Replace("{date}", local.ToString("dddd d MMMM", Fr))
            .Replace("{heure}", local.ToString("HH:mm", Fr));
        await notifications.NotifyAsync(user.Id, NotificationType.Reminder, "Rappel de rendez-vous", text, a.Id, a.OrganizationId, ct);
        await notifications.SendExternalAsync(user, text, ct);
    }
}
