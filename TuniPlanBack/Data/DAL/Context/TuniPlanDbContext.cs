using System.Linq.Expressions;
using Entities;
using Entities.Base;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

public class TuniPlanDbContext(DbContextOptions<TuniPlanDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<OrganizationPhoto> OrganizationPhotos => Set<OrganizationPhoto>();
    public DbSet<OpeningHour> OpeningHours => Set<OpeningHour>();
    public DbSet<ClosedPeriod> ClosedPeriods => Set<ClosedPeriod>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceResource> ServiceResources => Set<ServiceResource>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<OrganizationClient> OrganizationClients => Set<OrganizationClient>();

    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        // SQL Server datetime2 has no kind: every DateTime is stored as UTC and read back as DateTimeKind.Utc,
        // so the API serializes it with a trailing "Z" and the front-end converts it correctly.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TuniPlanDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Ids are created in code (Guid v7). ValueGeneratedNever => EF treats new objects added
            // to a tracked collection (e.g. org.Photos.Add(...)) as inserts, not updates.
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null && !entityType.IsOwned())
                modelBuilder.Entity(entityType.ClrType).Property(nameof(BaseEntity.Id)).ValueGeneratedNever();

            // SQL Server refuses multiple cascade paths: restrict everything by default,
            // configurations opt-in to cascade where it is safe.
            foreach (var fk in entityType.GetForeignKeys().Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade))
            {
                if (!CascadeAllowed.Contains((fk.PrincipalEntityType.ClrType, fk.DeclaringEntityType.ClrType)))
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
            }

            // Global soft-delete filter
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var body = Expression.Equal(
                    Expression.Property(parameter, nameof(ISoftDelete.IsDeleted)),
                    Expression.Constant(false));
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
            }
        }
    }

    private static readonly HashSet<(Type Principal, Type Dependent)> CascadeAllowed =
    [
        (typeof(User), typeof(RefreshToken)),
        (typeof(User), typeof(FamilyMember)),
        (typeof(User), typeof(Favorite)),
        (typeof(User), typeof(Notification)),
        (typeof(Organization), typeof(OrganizationPhoto)),
        (typeof(Organization), typeof(OpeningHour)),
        (typeof(Service), typeof(ServiceResource)),
        (typeof(Resource), typeof(ServiceResource)),
        (typeof(AiConversation), typeof(AiMessage)),
    ];
}
