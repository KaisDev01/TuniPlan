using BL.Interfaces;
using BL.Managers;
using DTOs.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers.Business;

/// <summary>Services (prestations), team/resources and promotions of a business.</summary>
[ApiController]
[Route("api/business/organizations/{orgId:guid}")]
[Authorize(Roles = $"{Roles.Business},{Roles.Admin}")]
public sealed class BusinessCatalogController(ICatalogManager catalog) : ControllerBase
{
    // -------- Services
    [HttpGet("services")]
    public async Task<ActionResult<IReadOnlyList<ServiceDto>>> Services(Guid orgId, CancellationToken ct) =>
        Ok(await catalog.GetServicesAsync(orgId, includeInactive: true, ct));

    [HttpPost("services")]
    public async Task<ActionResult<ServiceDto>> CreateService(Guid orgId, UpsertServiceRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateServiceAsync(orgId, request, ct));

    [HttpPut("services/{serviceId:guid}")]
    public async Task<ActionResult<ServiceDto>> UpdateService(Guid orgId, Guid serviceId, UpsertServiceRequest request, CancellationToken ct) =>
        Ok(await catalog.UpdateServiceAsync(orgId, serviceId, request, ct));

    [HttpDelete("services/{serviceId:guid}")]
    public async Task<IActionResult> DeleteService(Guid orgId, Guid serviceId, CancellationToken ct)
    {
        await catalog.DeleteServiceAsync(orgId, serviceId, ct);
        return NoContent();
    }

    // -------- Resources (team, rooms, vehicles)
    [HttpGet("resources")]
    public async Task<ActionResult<IReadOnlyList<ResourceDto>>> Resources(Guid orgId, CancellationToken ct) =>
        Ok(await catalog.GetResourcesAsync(orgId, includeInactive: true, ct));

    [HttpPost("resources")]
    public async Task<ActionResult<ResourceDto>> CreateResource(Guid orgId, UpsertResourceRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateResourceAsync(orgId, request, ct));

    [HttpPut("resources/{resourceId:guid}")]
    public async Task<ActionResult<ResourceDto>> UpdateResource(Guid orgId, Guid resourceId, UpsertResourceRequest request, CancellationToken ct) =>
        Ok(await catalog.UpdateResourceAsync(orgId, resourceId, request, ct));

    [HttpPost("resources/{resourceId:guid}/photo"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ResourceDto>> ResourcePhoto(Guid orgId, Guid resourceId, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await catalog.UploadResourcePhotoAsync(orgId, resourceId, stream, file.FileName, file.ContentType, ct));
    }

    [HttpDelete("resources/{resourceId:guid}")]
    public async Task<IActionResult> DeleteResource(Guid orgId, Guid resourceId, CancellationToken ct)
    {
        await catalog.DeleteResourceAsync(orgId, resourceId, ct);
        return NoContent();
    }

    // -------- Promotions
    [HttpGet("promotions")]
    public async Task<ActionResult<IReadOnlyList<PromotionDto>>> Promotions(Guid orgId, CancellationToken ct) =>
        Ok(await catalog.GetPromotionsAsync(orgId, activeOnly: false, ct));

    [HttpPost("promotions")]
    public async Task<ActionResult<PromotionDto>> CreatePromotion(Guid orgId, UpsertPromotionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreatePromotionAsync(orgId, request, ct));

    [HttpPut("promotions/{promotionId:guid}")]
    public async Task<ActionResult<PromotionDto>> UpdatePromotion(Guid orgId, Guid promotionId, UpsertPromotionRequest request, CancellationToken ct) =>
        Ok(await catalog.UpdatePromotionAsync(orgId, promotionId, request, ct));

    [HttpDelete("promotions/{promotionId:guid}")]
    public async Task<IActionResult> DeletePromotion(Guid orgId, Guid promotionId, CancellationToken ct)
    {
        await catalog.DeletePromotionAsync(orgId, promotionId, ct);
        return NoContent();
    }
}
