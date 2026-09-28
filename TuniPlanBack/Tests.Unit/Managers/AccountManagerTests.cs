using BL.Managers;
using Common.Security;
using DAL.Context;
using DAO;
using DTOs.Account;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Tests.Unit.Fakes;

namespace Tests.Unit.Managers;

public class AccountManagerTests : IAsyncLifetime
{
    private TuniPlanDbContext _db = default!;
    private AccountManager _account = default!;
    private readonly FakeCurrentUser _currentUser = new();
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private User _user = default!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create();
        var uow = new UnitOfWork(_db);
        _account = new AccountManager(uow, _currentUser, _hasher, new FakeFileStorage(), new FakeSessionCache(), new AuditManager(uow, _currentUser));
        _user = new User
        {
            FirstName = "Test", LastName = "Front", PhoneNumber = "+21699887766", Email = "test@example.com",
            PasswordHash = _hasher.Hash("MonMotDePasse2026"), PhoneConfirmed = true
        };
        var org = new Organization { Name = "Salon", Slug = "salon", Governorate = "Tunis", City = "Tunis", Address = "x" };
        var start = DateTime.UtcNow.AddDays(3);
        _db.AddRange(_user, org,
            new RefreshToken { UserId = _user.Id, TokenHash = "h", FamilyId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(30) },
            new UserDevice { UserId = _user.Id, Token = "ExponentPushToken[test]" },
            new Notification { UserId = _user.Id, Title = "t", Body = "b" },
            new Appointment { OrganizationId = org.Id, ServiceId = Guid.NewGuid(), ClientUserId = _user.Id, StartUtc = start, EndUtc = start.AddHours(1), Status = AppointmentStatus.Confirmed });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _currentUser.UserId = _user.Id;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Delete_account_anonymizes_and_keeps_history()
    {
        await _account.DeleteAccountAsync(new DeleteAccountRequest { Password = "MonMotDePasse2026" });
        _db.ChangeTracker.Clear();

        Assert.Empty(await _db.Users.ToListAsync());
        var deleted = await _db.Users.IgnoreQueryFilters().SingleAsync();
        Assert.True(deleted.IsDeleted);
        Assert.False(deleted.IsActive);
        Assert.Null(deleted.Email);
        Assert.NotEqual("+21699887766", deleted.PhoneNumber);
        Assert.NotNull(deleted.NotificationSettings); // owned columns kept (NOT NULL in SQL Server)
        Assert.NotNull((await _db.RefreshTokens.SingleAsync()).RevokedAt);
        Assert.Empty(await _db.UserDevices.ToListAsync());
        Assert.Equal(AppointmentStatus.CancelledByClient, (await _db.Appointments.SingleAsync()).Status);
        Assert.Single(await _db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Phone_number_can_be_reused_after_deletion()
    {
        await _account.DeleteAccountAsync(new DeleteAccountRequest { Password = "MonMotDePasse2026" });

        Assert.False(await new UnitOfWork(_db).Users.PhoneExistsAsync("+21699887766"));
    }
}
