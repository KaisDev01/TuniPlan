using Entities;
using Entities.Enums;

namespace DAO.Interfaces;

public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<User?> GetWithMembershipsAsync(Guid id, CancellationToken ct = default);
    Task<bool> PhoneExistsAsync(string phone, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
}

public interface IRefreshTokenRepository : IGenericRepository<RefreshToken>
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
}

public interface IOrganizationRepository : IGenericRepository<Organization>
{
    Task<Organization?> GetDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Organization?> GetDetailsBySlugAsync(string slug, CancellationToken ct = default);
    Task<Organization?> GetForEditAsync(Guid id, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<bool> IsMemberAsync(Guid organizationId, Guid userId, MemberRole? role = null, CancellationToken ct = default);
    Task<List<Organization>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    IQueryable<Organization> SearchPublished();
}

public interface IServiceRepository : IGenericRepository<Service>
{
    Task<List<Service>> GetByOrganizationAsync(Guid organizationId, bool activeOnly, CancellationToken ct = default);
    Task<Service?> GetWithResourcesAsync(Guid id, CancellationToken ct = default);
}

public interface IAppointmentRepository : IGenericRepository<Appointment>
{
    Task<Appointment?> GetDetailsAsync(Guid id, CancellationToken ct = default);
    /// <summary>Active bookings (pending/confirmed/counter-proposed) overlapping [fromUtc, toUtc).</summary>
    Task<List<Appointment>> GetBusyAsync(Guid organizationId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<bool> HasOverlapAsync(Guid organizationId, Guid? resourceId, DateTime startUtc, DateTime endUtc, Guid? excludeId = null, CancellationToken ct = default);
    IQueryable<Appointment> QueryWithDetails();
}

public interface IReviewRepository : IGenericRepository<Review>
{
    Task<(double Average, int Count)> GetRatingAsync(Guid organizationId, CancellationToken ct = default);
}
