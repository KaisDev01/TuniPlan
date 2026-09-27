using BL.Managers;
using Common.Helpers;
using DTOs.Appointments;
using DTOs.Business;
using DTOs.Common;
using DTOs.Reviews;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TuniPlan.Infrastructure;

namespace TuniPlan.Controllers;

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(IReviewManager reviews) : ControllerBase
{
    /// <summary>Verified review: only for a completed appointment of the current user.</summary>
    [HttpPost]
    public async Task<ActionResult<ReviewDto>> Create(CreateReviewRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await reviews.CreateAsync(request, ct));

    [HttpPost("{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, ReportReviewRequest request, CancellationToken ct)
    {
        await reviews.ReportAsync(id, request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(INotificationManager notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] PageQuery query, CancellationToken ct) =>
        Ok(await notifications.ListAsync(query.Page, query.PageSize, ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<CountDto>> UnreadCount(CancellationToken ct) => Ok(new CountDto(await notifications.UnreadCountAsync(ct)));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}

/// <summary>AI secretary: proposes slots, never confirms (the business owner confirms the request).</summary>
[ApiController]
[Route("api/ai-secretary")]
[EnableRateLimiting(ServiceCollectionExtensions.AiRateLimit)]
public sealed class AiSecretaryController(IAiSecretaryManager secretary) : ControllerBase
{
    [HttpPost("chat")]
    public async Task<ActionResult<AiChatResponse>> Chat(AiChatRequest request, CancellationToken ct) => Ok(await secretary.ChatAsync(request, ct));

    /// <summary>Sends the chosen slot as a PENDING request to the business.</summary>
    [HttpPost("requests")]
    public async Task<ActionResult<AppointmentDto>> CreateRequest(AiCreateRequestRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await secretary.CreateRequestAsync(request, ct));
}

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentManager payments) : ControllerBase
{
    /// <summary>Starts the deposit payment of an appointment. Returns a checkout URL.</summary>
    [HttpPost("deposit")]
    public async Task<ActionResult<PaymentDto>> Deposit(InitiateDepositRequest request, CancellationToken ct) =>
        Ok(await payments.InitiateDepositAsync(request, ct));

    /// <summary>Demo only (Security:EnableMockPayments): simulates the provider callback.</summary>
    [HttpPost("mock/confirm")]
    public async Task<ActionResult<PaymentDto>> ConfirmMock(ConfirmMockPaymentRequest request, CancellationToken ct) =>
        Ok(await payments.ConfirmMockAsync(request, ct));
}
