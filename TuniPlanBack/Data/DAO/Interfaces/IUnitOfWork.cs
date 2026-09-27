using System.Data;
using Entities;

namespace DAO.Interfaces;

/// <summary>
/// Unit of Work pattern: one DbContext shared by all repositories of a request,
/// a single SaveChanges and explicit transactions.
/// </summary>
public interface IUnitOfWork
{
    IUserRepository Users { get; }
    IRefreshTokenRepository RefreshTokens { get; }
    IOrganizationRepository Organizations { get; }
    IServiceRepository Services { get; }
    IAppointmentRepository Appointments { get; }
    IReviewRepository Reviews { get; }

    IGenericRepository<VerificationCode> VerificationCodes { get; }
    IGenericRepository<FamilyMember> FamilyMembers { get; }
    IGenericRepository<Favorite> Favorites { get; }
    IGenericRepository<AuditLog> AuditLogs { get; }
    IGenericRepository<OrganizationMember> OrganizationMembers { get; }
    IGenericRepository<OrganizationPhoto> OrganizationPhotos { get; }
    IGenericRepository<OpeningHour> OpeningHours { get; }
    IGenericRepository<ClosedPeriod> ClosedPeriods { get; }
    IGenericRepository<Resource> Resources { get; }
    IGenericRepository<ServiceResource> ServiceResources { get; }
    IGenericRepository<Promotion> Promotions { get; }
    IGenericRepository<OrganizationClient> OrganizationClients { get; }
    IGenericRepository<Payment> Payments { get; }
    IGenericRepository<WaitlistEntry> Waitlist { get; }
    IGenericRepository<Notification> Notifications { get; }
    IGenericRepository<AiConversation> AiConversations { get; }
    IGenericRepository<AiMessage> AiMessages { get; }

    /// <summary>Any other entity.</summary>
    IGenericRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the work inside a database transaction (with the SQL Server retry strategy).
    /// Commits when the delegate succeeds, rolls back on exception.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);
}
