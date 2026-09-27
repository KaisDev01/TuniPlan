using DAL.Context;
using DAO.Interfaces;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace DAO.Repositories;

public class UserRepository(TuniPlanDbContext context) : GenericRepository<User>(context), IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(u => u.Email == email.ToLower(), ct);

    public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);

    public Task<User?> GetWithMembershipsAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(u => u.Memberships).FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> PhoneExistsAsync(string phone, CancellationToken ct = default) =>
        Set.AnyAsync(u => u.PhoneNumber == phone, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        Set.AnyAsync(u => u.Email == email.ToLower(), ct);
}

public class RefreshTokenRepository(TuniPlanDbContext context) : GenericRepository<RefreshToken>(context), IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Set.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct = default)
    {
        var tokens = await Set.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) { t.RevokedAt = DateTime.UtcNow; t.RevokedReason = reason; }
    }

    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        var tokens = await Set.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) { t.RevokedAt = DateTime.UtcNow; t.RevokedReason = reason; }
    }
}

public class OrganizationRepository(TuniPlanDbContext context) : GenericRepository<Organization>(context), IOrganizationRepository
{
    private IQueryable<Organization> WithDetails() => Set.AsNoTracking()
        .Include(o => o.Photos)
        .Include(o => o.OpeningHours)
        .Include(o => o.Resources.Where(r => r.IsActive))
        .Include(o => o.Services.Where(s => s.IsActive)).ThenInclude(s => s.ServiceResources)
        .Include(o => o.Promotions.Where(p => p.IsActive));

    public Task<Organization?> GetDetailsAsync(Guid id, CancellationToken ct = default) =>
        WithDetails().FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Organization?> GetDetailsBySlugAsync(string slug, CancellationToken ct = default) =>
        WithDetails().FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public Task<Organization?> GetForEditAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(o => o.OpeningHours).Include(o => o.Photos).FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        Set.IgnoreQueryFilters().AnyAsync(o => o.Slug == slug, ct);

    public Task<bool> IsMemberAsync(Guid organizationId, Guid userId, MemberRole? role = null, CancellationToken ct = default) =>
        Context.OrganizationMembers.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId
                                                  && (role == null || m.Role == role), ct);

    public Task<List<Organization>> GetForUserAsync(Guid userId, CancellationToken ct = default) =>
        Set.AsNoTracking().Where(o => o.Members.Any(m => m.UserId == userId)).OrderBy(o => o.Name).ToListAsync(ct);

    public IQueryable<Organization> SearchPublished() =>
        Set.AsNoTracking().Where(o => o.IsPublished && o.ExternalBookingEnabled);
}

public class ServiceRepository(TuniPlanDbContext context) : GenericRepository<Service>(context), IServiceRepository
{
    public Task<List<Service>> GetByOrganizationAsync(Guid organizationId, bool activeOnly, CancellationToken ct = default) =>
        Set.AsNoTracking().Include(s => s.ServiceResources)
            .Where(s => s.OrganizationId == organizationId && (!activeOnly || s.IsActive))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);

    public Task<Service?> GetWithResourcesAsync(Guid id, CancellationToken ct = default) =>
        Set.Include(s => s.ServiceResources).FirstOrDefaultAsync(s => s.Id == id, ct);
}

public class AppointmentRepository(TuniPlanDbContext context) : GenericRepository<Appointment>(context), IAppointmentRepository
{
    private static readonly AppointmentStatus[] ActiveStatuses =
        [AppointmentStatus.Pending, AppointmentStatus.Confirmed, AppointmentStatus.CounterProposed];

    public IQueryable<Appointment> QueryWithDetails() => Set
        .Include(a => a.Organization)
        .Include(a => a.Service)
        .Include(a => a.Resource)
        .Include(a => a.ClientUser)
        .Include(a => a.FamilyMember)
        .Include(a => a.OrganizationClient)
        .Include(a => a.Review);

    public Task<Appointment?> GetDetailsAsync(Guid id, CancellationToken ct = default) =>
        QueryWithDetails().FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<Appointment>> GetBusyAsync(Guid organizationId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        Set.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && ActiveStatuses.Contains(a.Status)
                        && a.StartUtc < toUtc && a.EndUtc > fromUtc)
            .ToListAsync(ct);

    public Task<bool> HasOverlapAsync(Guid organizationId, Guid? resourceId, DateTime startUtc, DateTime endUtc,
        Guid? excludeId = null, CancellationToken ct = default) =>
        Set.AnyAsync(a => a.OrganizationId == organizationId
                          && (resourceId == null || a.ResourceId == resourceId)
                          && ActiveStatuses.Contains(a.Status)
                          && a.StartUtc < endUtc && a.EndUtc > startUtc
                          && (excludeId == null || a.Id != excludeId), ct);
}

public class ReviewRepository(TuniPlanDbContext context) : GenericRepository<Review>(context), IReviewRepository
{
    public async Task<(double Average, int Count)> GetRatingAsync(Guid organizationId, CancellationToken ct = default)
    {
        var q = Set.Where(r => r.OrganizationId == organizationId && !r.IsHidden);
        var count = await q.CountAsync(ct);
        if (count == 0) return (0, 0);
        var avg = await q.AverageAsync(r => (double)r.Rating, ct);
        return (Math.Round(avg, 1), count);
    }
}
