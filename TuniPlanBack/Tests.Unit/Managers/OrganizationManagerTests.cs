using BL.Managers;
using BL.Options;
using Common.Exceptions;
using DAL.Context;
using DAO;
using DTOs.Organizations;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Tests.Unit.Fakes;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tests.Unit.Managers;

public class OrganizationManagerTests : IAsyncLifetime
{
    private TuniPlanDbContext _db = default!;
    private OrganizationManager _organizations = default!;
    private readonly FakeCurrentUser _currentUser = new();
    private User _owner = default!, _colleague = default!;
    private Organization _org = default!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create();
        var uow = new UnitOfWork(_db);
        var clock = new FakeClock();
        var appOptions = MsOptions.Create(new AppOptions());
        var notifications = new NotificationManager(uow, _currentUser, new CapturingPush(), new CapturingSms(), new NullWhatsApp(), new NullLoggerManager());
        _organizations = new OrganizationManager(uow, _currentUser, new OrganizationAccess(uow, _currentUser),
            new AvailabilityManager(uow, _currentUser, clock, appOptions), new FakeFileStorage(), notifications,
            new AuditManager(uow, _currentUser), clock, appOptions);

        _owner = new User { FirstName = "Olfa", LastName = "G", PhoneNumber = "+21698000001", Email = "olfa@example.com", PasswordHash = "x", Roles = AccountRoles.Business };
        _colleague = new User { FirstName = "Lina", LastName = "B", PhoneNumber = "+21698000002", PasswordHash = "x", Roles = AccountRoles.Client };
        _org = new Organization { Name = "Salon", Slug = "salon", Governorate = "Tunis", City = "Tunis", Address = "x", IsPublished = true };
        _org.Members.Add(new OrganizationMember { UserId = _owner.Id, Role = MemberRole.Owner });
        _db.AddRange(_owner, _colleague, _org);
        await _db.SaveChangesAsync();
        _currentUser.UserId = _owner.Id;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Added_member_gets_business_role_and_last_owner_cannot_leave()
    {
        var member = await _organizations.AddMemberAsync(_org.Id, new AddMemberRequest { Identifier = "98 000 002" });

        Assert.Equal(MemberRole.Staff, member.Role);
        Assert.True((await _db.Users.SingleAsync(u => u.Id == _colleague.Id)).Roles.HasFlag(AccountRoles.Business));
        Assert.Equal(2, (await _organizations.GetMembersAsync(_org.Id)).Count);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => _organizations.RemoveMemberAsync(_org.Id, _owner.Id));
        Assert.Equal("last_owner", ex.Code);
    }

    [Fact]
    public async Task Transfer_makes_the_previous_owner_staff()
    {
        _colleague.Roles |= AccountRoles.Business;
        await _db.SaveChangesAsync();

        await _organizations.TransferOwnershipAsync(_org.Id, new TransferOrganizationRequest { NewOwnerIdentifier = "+21698000002" });
        _db.ChangeTracker.Clear();

        var members = await _db.OrganizationMembers.ToListAsync();
        Assert.Equal(MemberRole.Staff, members.Single(m => m.UserId == _owner.Id).Role);
        Assert.Equal(MemberRole.Owner, members.Single(m => m.UserId == _colleague.Id).Role);
    }

    [Fact]
    public async Task Delete_cancels_upcoming_bookings_and_frees_the_slug()
    {
        var client = new User { FirstName = "Sami", LastName = "K", PhoneNumber = "+21622000003", PasswordHash = "x" };
        var start = DateTime.UtcNow.AddDays(2);
        _db.AddRange(client, new Appointment
        {
            OrganizationId = _org.Id, ServiceId = Guid.NewGuid(), ClientUserId = client.Id, StartUtc = start, EndUtc = start.AddHours(1),
            Status = AppointmentStatus.Confirmed
        });
        await _db.SaveChangesAsync();

        await _organizations.DeleteAsync(_org.Id);
        _db.ChangeTracker.Clear();

        Assert.Empty(await _db.Organizations.ToListAsync());
        var deleted = await _db.Organizations.IgnoreQueryFilters().SingleAsync();
        Assert.StartsWith("deleted-", deleted.Slug);
        Assert.Empty(await _db.OrganizationMembers.ToListAsync());
        Assert.Equal(AppointmentStatus.CancelledByBusiness, (await _db.Appointments.SingleAsync()).Status);
    }
}
