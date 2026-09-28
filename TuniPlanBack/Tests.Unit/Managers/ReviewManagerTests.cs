using BL.Interfaces;
using BL.Managers;
using BL.Options;
using Common.Exceptions;
using DAL.Context;
using DAO;
using DTOs.Reviews;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using OperationStorage;
using Tests.Unit.Fakes;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tests.Unit.Managers;

public class ReviewManagerTests : IAsyncLifetime
{
    private TuniPlanDbContext _db = default!;
    private ReviewManager _reviews = default!;
    private NotificationManager _notifications = default!;
    private readonly FakeCurrentUser _currentUser = new();
    private readonly FakeClock _clock = new();
    private readonly FakeFileStorage _storage = new();
    private readonly CapturingPush _push = new();
    private User _client = default!, _other = default!, _owner = default!;
    private Organization _org = default!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create();
        var uow = new UnitOfWork(_db);
        _notifications = new NotificationManager(uow, _currentUser, _push, new CapturingSms(), new NullWhatsApp(), new NullLoggerManager());
        _reviews = new ReviewManager(uow, _currentUser, new OrganizationAccess(uow, _currentUser), _notifications, _storage,
            new AuditManager(uow, _currentUser), _clock, MsOptions.Create(new AppOptions { ReviewEditWindowDays = 30 }),
            MsOptions.Create(new StorageOptions()));

        User NewUser(string first, string phone) => new() { FirstName = first, LastName = "Test", PhoneNumber = phone, PasswordHash = "x", PhoneConfirmed = true };
        _client = NewUser("Sami", "+21622000011");
        _other = NewUser("Rim", "+21622000012");
        _owner = NewUser("Olfa", "+21622000013");
        _org = new Organization { Name = "Cabinet", Slug = "cabinet", Governorate = "Tunis", City = "Tunis", Address = "x", IsPublished = true };
        _org.Members.Add(new OrganizationMember { UserId = _owner.Id, Role = MemberRole.Owner });
        _db.AddRange(_client, _other, _owner, _org);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Appointment> CompletedAppointmentAsync()
    {
        var service = new Service { OrganizationId = _org.Id, Name = "Consultation", DurationMinutes = 30, Price = 50 };
        var start = DateTime.UtcNow.AddDays(-2);
        var appt = new Appointment
        {
            OrganizationId = _org.Id, ServiceId = service.Id, ClientUserId = _client.Id, StartUtc = start, EndUtc = start.AddMinutes(30),
            Status = AppointmentStatus.Completed
        };
        _db.AddRange(service, appt);
        await _db.SaveChangesAsync();
        return appt;
    }

    private static CreateReviewRequest Create(Guid appointmentId, int rating = 4, bool anonymous = false) => new()
    {
        AppointmentId = appointmentId, Rating = rating, RatingQuality = rating, Comment = "Très bonne consultation, je recommande.", IsAnonymous = anonymous
    };

    [Fact]
    public async Task Anonymous_review_hides_the_name_everywhere()
    {
        var appt = await CompletedAppointmentAsync();
        _currentUser.UserId = _client.Id;
        var created = await _reviews.CreateAsync(Create(appt.Id, anonymous: true));
        Assert.True(created.IsMine);
        Assert.Equal("Client anonyme", created.ClientName);

        _currentUser.UserId = _owner.Id;
        var forBusiness = await _reviews.GetForBusinessAsync(_org.Id, 1, 20);
        Assert.Equal("Client anonyme", forBusiness.Items.Single().Review.ClientName);
        Assert.False(forBusiness.Items.Single().Review.IsMine);
    }

    [Fact]
    public async Task Only_the_author_can_edit_or_delete()
    {
        var appt = await CompletedAppointmentAsync();
        _currentUser.UserId = _client.Id;
        var created = await _reviews.CreateAsync(Create(appt.Id));

        _currentUser.UserId = _other.Id;
        await Assert.ThrowsAsync<ForbiddenException>(() => _reviews.UpdateAsync(created.Id, new UpdateReviewRequest
        {
            Rating = 1, Comment = "Je modifie l'avis de quelqu'un d'autre."
        }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _reviews.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Edit_recomputes_rating_and_is_limited_in_time()
    {
        var appt = await CompletedAppointmentAsync();
        _currentUser.UserId = _client.Id;
        var created = await _reviews.CreateAsync(Create(appt.Id, rating: 5));

        var updated = await _reviews.UpdateAsync(created.Id, new UpdateReviewRequest { Rating = 2, Comment = "Finalement, attente beaucoup trop longue." });
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(2, (await _db.Organizations.SingleAsync()).RatingAverage);
        Assert.Equal(1, (await _reviews.GetSummaryAsync(_org.Id)).Distribution[2]);

        _clock.UtcNow = DateTime.UtcNow.AddDays(31);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _reviews.UpdateAsync(created.Id, new UpdateReviewRequest { Rating = 3, Comment = "Trop tard pour modifier cet avis." }));
        Assert.Equal("review_edit_window_over", ex.Code);
    }

    [Fact]
    public async Task Delete_resets_counters_and_allows_a_new_review()
    {
        var appt = await CompletedAppointmentAsync();
        _currentUser.UserId = _client.Id;
        var created = await _reviews.CreateAsync(Create(appt.Id));

        await _reviews.DeleteAsync(created.Id);
        _db.ChangeTracker.Clear();

        var org = await _db.Organizations.SingleAsync();
        Assert.Equal(0, org.ReviewCount);
        Assert.Equal(0, (await _reviews.GetSummaryAsync(_org.Id)).Count);
        var reloaded = await _db.Appointments.Include(a => a.Review).SingleAsync();
        Assert.Null(reloaded.Review); // hasReview = false
        Assert.Empty((await _reviews.GetMineAsync(1, 20)).Items);

        await _reviews.CreateAsync(Create(appt.Id, rating: 3));
    }

    [Fact]
    public async Task Photos_must_come_from_the_upload_route()
    {
        var appt = await CompletedAppointmentAsync();
        _currentUser.UserId = _client.Id;
        var request = Create(appt.Id) with { PhotoUrls = ["https://evil.example/photo.jpg"] };
        await Assert.ThrowsAsync<ValidationException>(() => _reviews.CreateAsync(request));

        var photo = await _reviews.UploadPhotoAsync(new MemoryStream([1, 2, 3]), "p.jpg", "image/jpeg");
        var created = await _reviews.CreateAsync(Create(appt.Id) with { PhotoUrls = [photo.Url] });
        Assert.Equal([photo.Url], created.PhotoUrls);
    }

    [Fact]
    public async Task Admin_sees_reported_reviews_and_can_delete_them()
    {
        var appt = await CompletedAppointmentAsync();
        _currentUser.UserId = _client.Id;
        var created = await _reviews.CreateAsync(Create(appt.Id, anonymous: true));
        _currentUser.UserId = _other.Id;
        await _reviews.ReportAsync(created.Id, new ReportReviewRequest { Reason = "Propos injurieux" });

        _currentUser.UserId = _owner.Id;
        _currentUser.RolesSet.Add(Roles.Admin);
        var reported = await _reviews.GetReportedAsync(1, 20);
        Assert.Equal(_client.Id, reported.Items.Single().ClientUserId);

        await _reviews.DeleteByAdminAsync(created.Id);
        Assert.Empty((await _reviews.GetReportedAsync(1, 20)).Items);
    }

    [Fact]
    public async Task Push_goes_to_registered_devices_and_forgets_dead_tokens()
    {
        _db.UserDevices.AddRange(
            new UserDevice { UserId = _client.Id, Token = "ExponentPushToken[alive]" },
            new UserDevice { UserId = _client.Id, Token = "ExponentPushToken[dead]" });
        await _db.SaveChangesAsync();
        _push.Unregistered.Add("ExponentPushToken[dead]");

        await _notifications.NotifyAsync(_client.Id, NotificationType.RequestAccepted, "Rendez-vous confirmé", "Demain 10:00", Guid.NewGuid());
        await _db.SaveChangesAsync();

        var sent = Assert.Single(_push.Sent);
        Assert.Equal(2, sent.Tokens.Count);
        Assert.Equal("RequestAccepted", sent.Message.Data!["type"]);
        Assert.Equal("ExponentPushToken[alive]", (await _db.UserDevices.SingleAsync()).Token);
    }
}
