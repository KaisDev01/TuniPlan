using System.Collections.Concurrent;
using System.Data;
using DAL.Context;
using DAO.Interfaces;
using DAO.Repositories;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DAO;

public sealed class UnitOfWork(TuniPlanDbContext context) : IUnitOfWork
{
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    private IUserRepository? _users;
    private IRefreshTokenRepository? _refreshTokens;
    private IOrganizationRepository? _organizations;
    private IServiceRepository? _services;
    private IAppointmentRepository? _appointments;
    private IReviewRepository? _reviews;

    public IUserRepository Users => _users ??= new UserRepository(context);
    public IRefreshTokenRepository RefreshTokens => _refreshTokens ??= new RefreshTokenRepository(context);
    public IOrganizationRepository Organizations => _organizations ??= new OrganizationRepository(context);
    public IServiceRepository Services => _services ??= new ServiceRepository(context);
    public IAppointmentRepository Appointments => _appointments ??= new AppointmentRepository(context);
    public IReviewRepository Reviews => _reviews ??= new ReviewRepository(context);

    public IGenericRepository<VerificationCode> VerificationCodes => Repository<VerificationCode>();
    public IGenericRepository<FamilyMember> FamilyMembers => Repository<FamilyMember>();
    public IGenericRepository<Favorite> Favorites => Repository<Favorite>();
    public IGenericRepository<AuditLog> AuditLogs => Repository<AuditLog>();
    public IGenericRepository<OrganizationMember> OrganizationMembers => Repository<OrganizationMember>();
    public IGenericRepository<OrganizationPhoto> OrganizationPhotos => Repository<OrganizationPhoto>();
    public IGenericRepository<OpeningHour> OpeningHours => Repository<OpeningHour>();
    public IGenericRepository<ClosedPeriod> ClosedPeriods => Repository<ClosedPeriod>();
    public IGenericRepository<Resource> Resources => Repository<Resource>();
    public IGenericRepository<ServiceResource> ServiceResources => Repository<ServiceResource>();
    public IGenericRepository<Promotion> Promotions => Repository<Promotion>();
    public IGenericRepository<OrganizationClient> OrganizationClients => Repository<OrganizationClient>();
    public IGenericRepository<Payment> Payments => Repository<Payment>();
    public IGenericRepository<WaitlistEntry> Waitlist => Repository<WaitlistEntry>();
    public IGenericRepository<Notification> Notifications => Repository<Notification>();
    public IGenericRepository<AiConversation> AiConversations => Repository<AiConversation>();
    public IGenericRepository<AiMessage> AiMessages => Repository<AiMessage>();
    public IGenericRepository<UserDevice> UserDevices => Repository<UserDevice>();
    public IGenericRepository<ExternalLogin> ExternalLogins => Repository<ExternalLogin>();

    public IGenericRepository<T> Repository<T>() where T : class =>
        (IGenericRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new GenericRepository<T>(context));

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        // Already inside a transaction: just run the work.
        if (context.Database.CurrentTransaction is not null) return await work(ct);

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction tx = await context.Database.BeginTransactionAsync(isolationLevel, ct);
            try
            {
                var result = await work(ct);
                await context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default) =>
        ExecuteInTransactionAsync<bool>(async token => { await work(token); return true; }, isolationLevel, ct);
}