using BL.Interfaces;
using BL.Managers;
using Common.Helpers;
using DTOs.Business;
using DTOs.Common;
using DTOs.Organizations;
using DTOs.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers.Business;

[ApiController]
[Route("api/business/organizations/{orgId:guid}/dashboard")]
[Authorize(Roles = $"{Roles.Business},{Roles.Admin}")]
public sealed class DashboardController(IDashboardManager dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(Guid orgId, CancellationToken ct) => Ok(await dashboard.GetAsync(orgId, ct));
}

/// <summary>Client files (CRM).</summary>
[ApiController]
[Route("api/business/organizations/{orgId:guid}/clients")]
[Authorize(Roles = $"{Roles.Business},{Roles.Admin}")]
public sealed class BusinessClientsController(IClientManager clients) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ClientSummaryDto>>> List(Guid orgId, [FromQuery] string? search, [FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await clients.ListAsync(orgId, search, page.Page, page.PageSize, ct));

    [HttpGet("{clientId:guid}")]
    public async Task<ActionResult<ClientDetailsDto>> Get(Guid orgId, Guid clientId, CancellationToken ct) =>
        Ok(await clients.GetAsync(orgId, clientId, ct));

    [HttpPost]
    public async Task<ActionResult<ClientSummaryDto>> Create(Guid orgId, CreateClientRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await clients.CreateAsync(orgId, request, ct));

    [HttpPut("{clientId:guid}")]
    public async Task<ActionResult<ClientDetailsDto>> Update(Guid orgId, Guid clientId, UpdateClientRequest request, CancellationToken ct) =>
        Ok(await clients.UpdateAsync(orgId, clientId, request, ct));
}

[ApiController]
[Route("api/business/organizations/{orgId:guid}/reviews")]
[Authorize(Roles = $"{Roles.Business},{Roles.Admin}")]
public sealed class BusinessReviewsController(IReviewManager reviews) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<BusinessReviewDto>>> List(Guid orgId, [FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await reviews.GetForBusinessAsync(orgId, page.Page, page.PageSize, ct));

    [HttpPost("{reviewId:guid}/reply")]
    public async Task<ActionResult<ReviewDto>> Reply(Guid orgId, Guid reviewId, ReplyReviewRequest request, CancellationToken ct) =>
        Ok(await reviews.ReplyAsync(orgId, reviewId, request, ct));
}

/// <summary>Platform administration: verification of businesses.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminController(IOrganizationManager organizations) : ControllerBase
{
    [HttpGet("verifications")]
    public async Task<ActionResult<IReadOnlyList<MyOrganizationDto>>> Pending(CancellationToken ct) =>
        Ok(await organizations.GetPendingVerificationsAsync(ct));

    [HttpPost("verifications/{orgId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid orgId, CancellationToken ct)
    {
        await organizations.SetVerificationStatusAsync(orgId, true, ct);
        return NoContent();
    }

    [HttpPost("verifications/{orgId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid orgId, CancellationToken ct)
    {
        await organizations.SetVerificationStatusAsync(orgId, false, ct);
        return NoContent();
    }
}
