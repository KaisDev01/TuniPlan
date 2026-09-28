using BL.Interfaces;
using BL.Mapping;
using BL.Options;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Reviews;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OperationStorage;

namespace BL.Managers;

public interface IReviewManager
{
    Task<ReviewDto> CreateAsync(CreateReviewRequest request, CancellationToken ct = default);
    Task<PagedResult<ReviewDto>> GetForOrganizationAsync(Guid organizationId, ReviewQuery query, CancellationToken ct = default);
    Task<ReviewSummaryDto> GetSummaryAsync(Guid organizationId, CancellationToken ct = default);
    Task ReportAsync(Guid reviewId, ReportReviewRequest request, CancellationToken ct = default);

    // Author
    Task<PagedResult<ReviewDto>> GetMineAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ReviewDto> UpdateAsync(Guid reviewId, UpdateReviewRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid reviewId, CancellationToken ct = default);
    Task<ReviewPhotoDto> UploadPhotoAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    // Business
    Task<PagedResult<BusinessReviewDto>> GetForBusinessAsync(Guid organizationId, int page, int pageSize, CancellationToken ct = default);
    Task<ReviewDto> ReplyAsync(Guid organizationId, Guid reviewId, ReplyReviewRequest request, CancellationToken ct = default);

    // Admin
    Task<PagedResult<AdminReviewDto>> GetReportedAsync(int page, int pageSize, CancellationToken ct = default);
    Task DeleteByAdminAsync(Guid reviewId, CancellationToken ct = default);
}

/// <summary>Verified reviews: only a client with a completed appointment can review, once per appointment.</summary>
public sealed class ReviewManager(
    IUnitOfWork uow, ICurrentUser currentUser, IOrganizationAccess access, INotificationManager notifications,
    IFileStorage storage, IAuditManager audit, IClock clock, IOptions<AppOptions> appOptions, IOptions<StorageOptions> storageOptions) : IReviewManager
{
    private const int ReviewWindowDays = 60;
    private int EditWindowDays => appOptions.Value.ReviewEditWindowDays;

    private ReviewDto Map(Review r) => r.ToDto(currentUser.UserId, EditWindowDays);

    private IQueryable<Review> WithDetails(IQueryable<Review> q) =>
        q.Include(r => r.ClientUser).Include(r => r.Organization).Include(r => r.Appointment).ThenInclude(a => a.Service);

    public async Task<ReviewDto> CreateAsync(CreateReviewRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var appt = await uow.Appointments.QueryWithDetails().FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct)
                   ?? throw new NotFoundException("Rendez-vous introuvable.");
        if (appt.ClientUserId != userId) throw new ForbiddenException("Ce rendez-vous ne vous appartient pas.");
        if (appt.Status != AppointmentStatus.Completed)
            throw new BusinessRuleException("Vous pourrez laisser un avis après votre rendez-vous.", "appointment_not_completed");
        if (appt.Review is not null) throw new ConflictException("Vous avez déjà laissé un avis pour ce rendez-vous.");
        if ((clock.UtcNow - appt.StartUtc).TotalDays > ReviewWindowDays)
            throw new BusinessRuleException($"Les avis sont possibles pendant {ReviewWindowDays} jours après le rendez-vous.");
        var photos = ValidatePhotos(request.PhotoUrls, userId);

        var review = new Review
        {
            OrganizationId = appt.OrganizationId, AppointmentId = appt.Id, ClientUserId = userId,
            Rating = request.Rating, RatingWelcome = request.RatingWelcome, RatingPunctuality = request.RatingPunctuality,
            RatingQuality = request.RatingQuality, RatingValue = request.RatingValue,
            Comment = request.Comment.Trim(), PhotoUrls = photos, IsAnonymous = request.IsAnonymous
        };
        await uow.Reviews.AddAsync(review, ct);
        await uow.SaveChangesAsync(ct);
        await RecomputeRatingAsync(appt.OrganizationId, ct);
        await notifications.NotifyOrganizationAsync(appt.OrganizationId, NotificationType.NewReview,
            $"Nouvel avis {request.Rating}★", review.Comment.Length > 120 ? review.Comment[..120] + "…" : review.Comment, appt.Id, ct);
        await uow.SaveChangesAsync(ct);

        review.ClientUser = appt.ClientUser!;
        review.Appointment = appt;
        review.Organization = appt.Organization;
        return Map(review);
    }

    public async Task<PagedResult<ReviewDto>> GetForOrganizationAsync(Guid organizationId, ReviewQuery query, CancellationToken ct = default)
    {
        var q = WithDetails(uow.Reviews.QueryNoTracking()).Where(r => r.OrganizationId == organizationId && !r.IsHidden);
        if (query.WithPhotos) q = q.Where(r => r.PhotoUrls.Count > 0);
        if (query.ServiceId is { } sid) q = q.Where(r => r.Appointment.ServiceId == sid);
        q = query.Sort switch
        {
            "best" => q.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "worst" => q.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _ => q.OrderByDescending(r => r.CreatedAt)
        };
        var total = await q.CountAsync(ct);
        var items = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PagedResult<ReviewDto>(items.Select(Map).ToList(), query.Page, query.PageSize, total);
    }

    public Task<ReviewSummaryDto> GetSummaryAsync(Guid organizationId, CancellationToken ct = default) =>
        BuildSummaryAsync(uow.Reviews.QueryNoTracking().Where(r => r.OrganizationId == organizationId && !r.IsHidden), ct);

    internal static async Task<ReviewSummaryDto> BuildSummaryAsync(IQueryable<Review> reviews, CancellationToken ct)
    {
        var rows = await reviews.Select(r => new { r.Rating, r.RatingWelcome, r.RatingPunctuality, r.RatingQuality, r.RatingValue }).ToListAsync(ct);
        var distribution = Enumerable.Range(1, 5).ToDictionary(i => i, i => rows.Count(r => r.Rating == i));
        double? Avg(IEnumerable<int?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count == 0 ? null : Math.Round(list.Average(), 1);
        }
        return new ReviewSummaryDto
        {
            Average = rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.Rating), 1),
            Count = rows.Count,
            Distribution = distribution,
            AverageWelcome = Avg(rows.Select(r => r.RatingWelcome)),
            AveragePunctuality = Avg(rows.Select(r => r.RatingPunctuality)),
            AverageQuality = Avg(rows.Select(r => r.RatingQuality)),
            AverageValue = Avg(rows.Select(r => r.RatingValue))
        };
    }

    public async Task ReportAsync(Guid reviewId, ReportReviewRequest request, CancellationToken ct = default)
    {
        currentUser.RequireUserId();
        var review = await uow.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct) ?? throw new NotFoundException("Avis introuvable.");
        review.IsReported = true;
        review.ReportReason = request.Reason.Trim();
        await uow.SaveChangesAsync(ct);
    }

    // =========================================================== Author
    public async Task<PagedResult<ReviewDto>> GetMineAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var q = WithDetails(uow.Reviews.QueryNoTracking()).Where(r => r.ClientUserId == userId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<ReviewDto>(items.Select(Map).ToList(), page, pageSize, total);
    }

    public async Task<ReviewDto> UpdateAsync(Guid reviewId, UpdateReviewRequest request, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var review = await WithDetails(uow.Reviews.Query()).FirstOrDefaultAsync(r => r.Id == reviewId, ct)
                     ?? throw new NotFoundException("Avis introuvable.");
        if (review.ClientUserId != userId) throw new ForbiddenException("Vous ne pouvez modifier que vos propres avis.");
        if (clock.UtcNow > review.CreatedAt.AddDays(EditWindowDays))
            throw new BusinessRuleException($"Un avis peut être modifié pendant {EditWindowDays} jours après sa publication.", "review_edit_window_over");
        var photos = ValidatePhotos(request.PhotoUrls, userId);

        var removedPhotos = review.PhotoUrls.Except(photos).ToList();
        review.Rating = request.Rating;
        review.RatingWelcome = request.RatingWelcome;
        review.RatingPunctuality = request.RatingPunctuality;
        review.RatingQuality = request.RatingQuality;
        review.RatingValue = request.RatingValue;
        review.Comment = request.Comment.Trim();
        review.PhotoUrls = photos;
        review.IsAnonymous = request.IsAnonymous;
        review.EditedAt = clock.UtcNow;
        await uow.SaveChangesAsync(ct);
        await RecomputeRatingAsync(review.OrganizationId, ct);
        await uow.SaveChangesAsync(ct);
        foreach (var url in removedPhotos) await storage.DeleteAsync(url, ct);
        return Map(review);
    }

    public async Task DeleteAsync(Guid reviewId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        var review = await uow.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct) ?? throw new NotFoundException("Avis introuvable.");
        if (review.ClientUserId != userId) throw new ForbiddenException("Vous ne pouvez supprimer que vos propres avis.");
        await SoftDeleteAsync(review, "review_deleted", ct);
    }

    public async Task<ReviewPhotoDto> UploadPhotoAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var userId = currentUser.RequireUserId();
        try
        {
            var stored = await storage.SaveImageAsync(content, fileName, contentType, PhotoFolder(userId), ct);
            return new ReviewPhotoDto(stored.Url);
        }
        catch (InvalidFileException ex) { throw new BadRequestException(ex.Message, "invalid_file"); }
    }

    private static string PhotoFolder(Guid userId) => $"reviews-{userId:N}";

    /// <summary>Only photos uploaded by the author through POST /api/reviews/photos are accepted.</summary>
    private List<string> ValidatePhotos(IReadOnlyList<string> urls, Guid userId)
    {
        var prefix = $"{storageOptions.Value.PublicBaseUrl.TrimEnd('/')}/{PhotoFolder(userId)}/";
        var list = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).Distinct().ToList();
        if (list.Count > 3) throw ValidationException.For("PhotoUrls", "3 photos maximum.");
        if (list.Any(u => !u.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || u.Contains("..")))
            throw ValidationException.For("PhotoUrls", "Envoyez d'abord les photos avec POST /api/reviews/photos.");
        return list;
    }

    // =========================================================== Business
    public async Task<PagedResult<BusinessReviewDto>> GetForBusinessAsync(Guid organizationId, int page, int pageSize, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var q = WithDetails(uow.Reviews.QueryNoTracking()).Where(r => r.OrganizationId == organizationId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var weekAgo = clock.UtcNow.AddDays(-7);
        return new PagedResult<BusinessReviewDto>(items.Select(r => new BusinessReviewDto
        {
            Review = Map(r), IsNew = r.CreatedAt >= weekAgo && r.OwnerReply is null, IsReported = r.IsReported
        }).ToList(), page, pageSize, total);
    }

    public async Task<ReviewDto> ReplyAsync(Guid organizationId, Guid reviewId, ReplyReviewRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var review = await WithDetails(uow.Reviews.Query())
                         .FirstOrDefaultAsync(r => r.Id == reviewId && r.OrganizationId == organizationId, ct)
                     ?? throw new NotFoundException("Avis introuvable.");
        var isNewReply = review.OwnerReply is null;
        review.OwnerReply = request.Reply.Trim();
        review.OwnerReplyAt = clock.UtcNow;
        if (isNewReply)
            await notifications.NotifyAsync(review.ClientUserId, NotificationType.General, "Réponse à votre avis",
                "L'établissement a répondu à votre avis.", review.AppointmentId, organizationId, ct);
        await uow.SaveChangesAsync(ct);
        return Map(review);
    }

    // =========================================================== Admin
    public async Task<PagedResult<AdminReviewDto>> GetReportedAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin)) throw new ForbiddenException();
        var q = WithDetails(uow.Reviews.QueryNoTracking()).Where(r => r.IsReported);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AdminReviewDto>(items.Select(r => new AdminReviewDto
        {
            Review = Map(r), ClientUserId = r.ClientUserId, ReportReason = r.ReportReason, IsHidden = r.IsHidden
        }).ToList(), page, pageSize, total);
    }

    public async Task DeleteByAdminAsync(Guid reviewId, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin)) throw new ForbiddenException();
        var review = await uow.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct) ?? throw new NotFoundException("Avis introuvable.");
        await SoftDeleteAsync(review, "review_deleted_by_admin", ct);
    }

    /// <summary>Soft delete: the appointment gets HasReview = false again, and the business rating is recomputed.</summary>
    private async Task SoftDeleteAsync(Review review, string auditAction, CancellationToken ct)
    {
        var photos = review.PhotoUrls.ToList();
        review.IsDeleted = true;
        review.DeletedAt = clock.UtcNow;
        await audit.AddAsync(auditAction, currentUser.UserId, review.Id.ToString(), ct);
        await uow.SaveChangesAsync(ct);
        await RecomputeRatingAsync(review.OrganizationId, ct);
        await uow.SaveChangesAsync(ct);
        foreach (var url in photos) await storage.DeleteAsync(url, ct);
    }

    /// <summary>Average and count stored on the business (distribution and sub-ratings are computed live from the reviews).</summary>
    private async Task RecomputeRatingAsync(Guid organizationId, CancellationToken ct)
    {
        var (avg, count) = await uow.Reviews.GetRatingAsync(organizationId, ct);
        var org = await uow.Organizations.GetByIdAsync(organizationId, ct);
        if (org is null) return;
        org.RatingAverage = avg;
        org.ReviewCount = count;
    }
}
