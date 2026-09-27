using BL.Interfaces;
using BL.Mapping;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Business;
using Entities;
using Entities.Enums;
using LoggerService;
using Microsoft.EntityFrameworkCore;
using NotificationService;

namespace BL.Managers;

// ---------------------------------------------------------------- Audit
public interface IAuditManager
{
    /// <summary>Adds an audit entry (saved with the next SaveChanges).</summary>
    Task AddAsync(string action, Guid? userId, string? details = null, CancellationToken ct = default);
}

public sealed class AuditManager(IUnitOfWork uow, ICurrentUser currentUser) : IAuditManager
{
    public Task AddAsync(string action, Guid? userId, string? details = null, CancellationToken ct = default) =>
        uow.AuditLogs.AddAsync(new AuditLog
        {
            Action = action, UserId = userId, Details = details?.Length > 1000 ? details[..1000] : details,
            IpAddress = currentUser.IpAddress, UserAgent = currentUser.UserAgent?.Length > 300 ? currentUser.UserAgent[..300] : currentUser.UserAgent
        }, ct);
}

// ---------------------------------------------------------------- Organization access
public interface IOrganizationAccess
{
    /// <summary>Throws 403 unless the current user is a member (or owner when <paramref name="ownerOnly"/>) of the organization. Admins pass.</summary>
    Task EnsureMemberAsync(Guid organizationId, bool ownerOnly = false, CancellationToken ct = default);
}

public sealed class OrganizationAccess(IUnitOfWork uow, ICurrentUser currentUser) : IOrganizationAccess
{
    public async Task EnsureMemberAsync(Guid organizationId, bool ownerOnly = false, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        if (currentUser.IsInRole(Roles.Admin)) return;
        if (!await uow.Organizations.AnyAsync(o => o.Id == organizationId, ct)) throw new NotFoundException("Entreprise introuvable.");
        var ok = await uow.Organizations.IsMemberAsync(organizationId, userId, ownerOnly ? (MemberRole?)MemberRole.Owner : null, ct);
        if (!ok) throw new ForbiddenException(ownerOnly
            ? "Seul le propriétaire peut modifier ce paramètre."
            : "Vous n'avez pas accès à cette entreprise.");
    }
}

// ---------------------------------------------------------------- Notifications
public interface INotificationManager
{
    /// <summary>Adds an in-app notification (saved with the next SaveChanges) and sends a push if the user allows it.</summary>
    Task NotifyAsync(Guid userId, NotificationType type, string title, string body, Guid? appointmentId = null,
        Guid? organizationId = null, CancellationToken ct = default);

    /// <summary>Notifies every member (owner + staff) of an organization.</summary>
    Task NotifyOrganizationAsync(Guid organizationId, NotificationType type, string title, string body,
        Guid? appointmentId = null, CancellationToken ct = default);

    /// <summary>Sends SMS / WhatsApp according to the user's preferences.</summary>
    Task SendExternalAsync(User user, string body, CancellationToken ct = default);

    Task<PagedResult<NotificationDto>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> UnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}

public sealed class NotificationManager(
    IUnitOfWork uow, ICurrentUser currentUser, IPushSender push, ISmsSender sms, IWhatsAppSender whatsApp,
    ILoggerManager logger) : INotificationManager
{
    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string body, Guid? appointmentId = null,
        Guid? organizationId = null, CancellationToken ct = default)
    {
        await uow.Notifications.AddAsync(new Notification
        {
            UserId = userId, Type = type, Title = title, Body = body, AppointmentId = appointmentId, OrganizationId = organizationId
        }, ct);

        var user = await uow.Users.GetByIdAsync(userId, ct);
        if (user?.NotificationSettings.PushEnabled == true)
        {
            try { await push.SendAsync(userId, title, body, ct); }
            catch (Exception ex) { logger.LogError(ex, "Push failed for {UserId}", userId); }
        }
    }

    public async Task NotifyOrganizationAsync(Guid organizationId, NotificationType type, string title, string body,
        Guid? appointmentId = null, CancellationToken ct = default)
    {
        var memberIds = await uow.OrganizationMembers.QueryNoTracking()
            .Where(m => m.OrganizationId == organizationId).Select(m => m.UserId).ToListAsync(ct);
        foreach (var id in memberIds.Distinct())
            await NotifyAsync(id, type, title, body, appointmentId, organizationId, ct);
    }

    public async Task SendExternalAsync(User user, string body, CancellationToken ct = default)
    {
        try
        {
            if (user.NotificationSettings.SmsEnabled) await sms.SendAsync(new OutgoingMessage(user.PhoneNumber, body), ct);
            if (user.NotificationSettings.WhatsAppEnabled) await whatsApp.SendAsync(new OutgoingMessage(user.PhoneNumber, body), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "External notification failed for {UserId}", user.Id);
        }
    }

    public async Task<PagedResult<NotificationDto>> ListAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var q = uow.Notifications.QueryNoTracking().Where(n => n.UserId == userId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<NotificationDto>(items.Select(n => n.ToDto()).ToList(), page, pageSize, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        return uow.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var n = await uow.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
                ?? throw new NotFoundException("Notification introuvable.");
        n.ReadAt ??= DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var unread = await uow.Notifications.Query().Where(n => n.UserId == userId && n.ReadAt == null).ToListAsync(ct);
        foreach (var n in unread) n.ReadAt = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
    }
}
