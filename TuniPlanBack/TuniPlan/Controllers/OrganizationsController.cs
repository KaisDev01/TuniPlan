using BL.Managers;
using Common.Helpers;
using DTOs.Catalog;
using DTOs.Organizations;
using DTOs.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers;

/// <summary>Public side: search businesses, see a business page (description, reviews, slots).</summary>
[ApiController]
[Route("api/organizations")]
[AllowAnonymous]
public sealed class OrganizationsController(
    IOrganizationManager organizations, ICatalogManager catalog, IAvailabilityManager availability, IReviewManager reviews) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrganizationCardDto>>> Search([FromQuery] OrganizationSearchQuery query, CancellationToken ct) =>
        Ok(await organizations.SearchAsync(query, ct));

    /// <summary>Full business page: photos, description, team, services with prices, hours, review summary, next slots.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationDetailsDto>> Get(Guid id, CancellationToken ct) => Ok(await organizations.GetPublicAsync(id, ct));

    /// <summary>Public link: /b/{slug}.</summary>
    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<OrganizationDetailsDto>> GetBySlug(string slug, CancellationToken ct) =>
        Ok(await organizations.GetPublicBySlugAsync(slug, ct));

    [HttpGet("{id:guid}/services")]
    public async Task<ActionResult<IReadOnlyList<ServiceDto>>> Services(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetServicesAsync(id, includeInactive: false, ct));

    [HttpGet("{id:guid}/team")]
    public async Task<ActionResult<IReadOnlyList<ResourceDto>>> Team(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetResourcesAsync(id, includeInactive: false, ct));

    [HttpGet("{id:guid}/promotions")]
    public async Task<ActionResult<IReadOnlyList<PromotionDto>>> Promotions(Guid id, CancellationToken ct) =>
        Ok(await catalog.GetPromotionsAsync(id, activeOnly: true, ct));

    /// <summary>Free slots per day for a service (optionally for one team member).</summary>
    [HttpGet("{id:guid}/availability")]
    public async Task<ActionResult<IReadOnlyList<DayAvailabilityDto>>> Availability(Guid id, [FromQuery] AvailabilityQuery query, CancellationToken ct) =>
        Ok(await availability.GetAvailabilityAsync(id, query, ct));

    [HttpGet("{id:guid}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> Reviews(Guid id, [FromQuery] ReviewQuery query, CancellationToken ct) =>
        Ok(await reviews.GetForOrganizationAsync(id, query, ct));

    [HttpGet("{id:guid}/reviews/summary")]
    public async Task<ActionResult<ReviewSummaryDto>> ReviewSummary(Guid id, CancellationToken ct) =>
        Ok(await reviews.GetSummaryAsync(id, ct));
}
