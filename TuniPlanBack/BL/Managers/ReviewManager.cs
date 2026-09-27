using BL.Interfaces;
using BL.Mapping;
using Common.Exceptions;
using Common.Helpers;
using DAO.Interfaces;
using DTOs.Reviews;
using Entities;
using Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace BL.Managers;

public interface IReviewManager
{
    Task<ReviewDto> CreateAsync(CreateReviewRequest request, CancellationToken ct = default);
    Task<PagedResult<ReviewDto>> GetForOrganizationAsync(Guid organizationId, ReviewQuery query, CancellationToken ct = default);
    Task<ReviewSummaryDto> GetSummaryAsync(Guid organizationId, CancellationToken ct = default);
    Task ReportAsync(Guid reviewId, ReportReviewRequest request, CancellationToken ct = default);

    Task<PagedResult<BusinessReviewDto>> GetForBusinessAsync(Guid organizationId, int page, int pageSize, CancellationToken ct = default);
    Task<ReviewDto> ReplyAsync(Guid organizationId, Guid reviewId, ReplyReviewRequest request, CancellationToken ct = default);
}

/// <summary>Verified reviews: only a client with a completed appointment can review, once per appointment.</summary>
public sealed class ReviewManager(
    IUnitOfWork uow, ICurrentUser currentUser, IOrganizationAccess access, INotificationManager notifications, IClock clock) : IReviewManager
{
    private const int ReviewWindowDays = 60;

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

        var review = new Review
        {
            OrganizationId = appt.OrganizationId, AppointmentId = appt.Id, ClientUserId = userId,
            Rating = request.Rating, RatingWelcome = request.RatingWelcome, RatingPunctuality = request.RatingPunctuality,
            RatingQuality = request.RatingQuality, RatingValue = request.RatingValue,
            Comment = request.Comment.Trim(), PhotoUrls = request.PhotoUrls.Take(3).ToList()
        };
        await uow.Reviews.AddAsync(review, ct);
        await uow.SaveChangesAsync(ct);
        await RecomputeRatingAsync(appt.OrganizationId, ct);
        await notifications.NotifyOrganizationAsync(appt.OrganizationId, NotificationType.NewReview,
            $"Nouvel avis {request.Rating}★", review.Comment.Length > 120 ? review.Comment[..120] + "…" : review.Comment, appt.Id, ct);
        await uow.SaveChangesAsync(ct);

        review.ClientUser = appt.ClientUser!;
        review.Appointment = appt;
        return review.ToDto();
    }

    public async Task<PagedResult<ReviewDto>> GetForOrganizationAsync(Guid organizationId, ReviewQuery query, CancellationToken ct = default)
    {
        var q = uow.Reviews.QueryNoTracking()
            .Include(r => r.ClientUser).Include(r => r.Appointment).ThenInclude(a => a.Service)
            .Where(r => r.OrganizationId == organizationId && !r.IsHidden);
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
        return new PagedResult<ReviewDto>(items.Select(r => r.ToDto()).ToList(), query.Page, query.PageSize, total);
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
        var review = await uow.Reviews.GetByIdAsync(reviewId, ct) ?? throw new NotFoundException("Avis introuvable.");
        review.IsReported = true;
        review.ReportReason = request.Reason.Trim();
        await uow.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<BusinessReviewDto>> GetForBusinessAsync(Guid organizationId, int page, int pageSize, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var q = uow.Reviews.QueryNoTracking()
            .Include(r => r.ClientUser).Include(r => r.Appointment).ThenInclude(a => a.Service)
            .Where(r => r.OrganizationId == organizationId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var weekAgo = clock.UtcNow.AddDays(-7);
        return new PagedResult<BusinessReviewDto>(items.Select(r => new BusinessReviewDto
        {
            Review = r.ToDto(), IsNew = r.CreatedAt >= weekAgo && r.OwnerReply is null, IsReported = r.IsReported
        }).ToList(), page, pageSize, total);
    }

    public async Task<ReviewDto> ReplyAsync(Guid organizationId, Guid reviewId, ReplyReviewRequest request, CancellationToken ct = default)
    {
        await access.EnsureMemberAsync(organizationId, ct: ct);
        var review = await uow.Reviews.Query().Include(r => r.ClientUser).Include(r => r.Appointment).ThenInclude(a => a.Service)
                         .FirstOrDefaultAsync(r => r.Id == reviewId && r.OrganizationId == organizationId, ct)
                     ?? throw new NotFoundException("Avis introuvable.");
        var isNewReply = review.OwnerReply is null;
        review.OwnerReply = request.Reply.Trim();
        review.OwnerReplyAt = clock.UtcNow;
        if (isNewReply)
            await notifications.NotifyAsync(review.ClientUserId, NotificationType.General, "Réponse à votre avis",
                "L'établissement a répondu à votre avis.", review.AppointmentId, organizationId, ct);
        await uow.SaveChangesAsync(ct);
        return review.ToDto();
    }

    private async Task RecomputeRatingAsync(Guid organizationId, CancellationToken ct)
    {
        var (avg, count) = await uow.Reviews.GetRatingAsync(organizationId, ct);
        var org = await uow.Organizations.GetByIdAsync(organizationId, ct);
        if (org is null) return;
        org.RatingAverage = avg;
        org.ReviewCount = count;
    }
}
