using BL.Interfaces;
using BL.Managers;
using DTOs.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers.Business;

/// <summary>Business sign-up wizard and business settings (info, photos, hours, rules, verification, publication).</summary>
[ApiController]
[Route("api/business/organizations")]
[Authorize(Roles = $"{Roles.Business},{Roles.Admin}")]
public sealed class BusinessOrganizationsController(IOrganizationManager organizations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationCardDto>>> Mine(CancellationToken ct) => Ok(await organizations.GetMineAsync(ct));

    /// <summary>Wizard step "Entreprise": creates the business (unpublished).</summary>
    [HttpPost]
    public async Task<ActionResult<MyOrganizationDto>> Create(UpsertOrganizationRequest request, CancellationToken ct)
    {
        var result = await organizations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { orgId = result.Details.Id }, result);
    }

    [HttpGet("{orgId:guid}")]
    public async Task<ActionResult<MyOrganizationDto>> Get(Guid orgId, CancellationToken ct) => Ok(await organizations.GetMyOrganizationAsync(orgId, ct));

    [HttpPut("{orgId:guid}")]
    public async Task<ActionResult<MyOrganizationDto>> Update(Guid orgId, UpsertOrganizationRequest request, CancellationToken ct) =>
        Ok(await organizations.UpdateAsync(orgId, request, ct));

    /// <summary>Owner only. Deletes the business: upcoming bookings are cancelled and clients notified.</summary>
    [HttpDelete("{orgId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, CancellationToken ct)
    {
        await organizations.DeleteAsync(orgId, ct);
        return NoContent();
    }

    /// <summary>Owner only. Gives the business to another account (email or phone); the current owner stays as staff.</summary>
    [HttpPost("{orgId:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid orgId, TransferOrganizationRequest request, CancellationToken ct)
    {
        await organizations.TransferOwnershipAsync(orgId, request, ct);
        return NoContent();
    }

    /// <summary>Team of the business (owners and staff).</summary>
    [HttpGet("{orgId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> Members(Guid orgId, CancellationToken ct) =>
        Ok(await organizations.GetMembersAsync(orgId, ct));

    /// <summary>Owner only. Adds an existing account (email or phone) as Staff or Owner.</summary>
    [HttpPost("{orgId:guid}/members")]
    public async Task<ActionResult<MemberDto>> AddMember(Guid orgId, AddMemberRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await organizations.AddMemberAsync(orgId, request, ct));

    /// <summary>Owner only, or the member himself (leave the team). The last owner cannot be removed.</summary>
    [HttpDelete("{orgId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid orgId, Guid userId, CancellationToken ct)
    {
        await organizations.RemoveMemberAsync(orgId, userId, ct);
        return NoContent();
    }

    /// <summary>Wizard step "Horaires".</summary>
    [HttpPut("{orgId:guid}/opening-hours")]
    public async Task<ActionResult<MyOrganizationDto>> OpeningHours(Guid orgId, IReadOnlyList<OpeningHourDto> hours, CancellationToken ct) =>
        Ok(await organizations.UpdateOpeningHoursAsync(orgId, hours, ct));

    /// <summary>Wizard step "Règles de réservation".</summary>
    [HttpPut("{orgId:guid}/rules")]
    public async Task<ActionResult<MyOrganizationDto>> Rules(Guid orgId, BookingRulesDto rules, CancellationToken ct) =>
        Ok(await organizations.UpdateRulesAsync(orgId, rules, ct));

    /// <summary>Wizard step "Vérification" (matricule fiscal / RNE).</summary>
    [HttpPost("{orgId:guid}/verification")]
    public async Task<ActionResult<MyOrganizationDto>> Verification(Guid orgId, VerificationRequest request, CancellationToken ct) =>
        Ok(await organizations.SubmitVerificationAsync(orgId, request, ct));

    [HttpPost("{orgId:guid}/publish")]
    public async Task<ActionResult<MyOrganizationDto>> Publish(Guid orgId, CancellationToken ct) =>
        Ok(await organizations.SetPublishedAsync(orgId, true, ct));

    [HttpPost("{orgId:guid}/unpublish")]
    public async Task<ActionResult<MyOrganizationDto>> Unpublish(Guid orgId, CancellationToken ct) =>
        Ok(await organizations.SetPublishedAsync(orgId, false, ct));

    /// <summary>Upload an image. kind = logo | cover | photo (case-insensitive). JPG, PNG or WEBP, 5 MB max, multipart field "file".</summary>
    [HttpPost("{orgId:guid}/images/{kind}"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<MyOrganizationDto>> UploadImage(Guid orgId, ImageKind kind, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await organizations.UploadImageAsync(orgId, kind.ToString().ToLowerInvariant(), stream, file.FileName, file.ContentType, ct));
    }

    [HttpDelete("{orgId:guid}/photos")]
    public async Task<ActionResult<MyOrganizationDto>> DeletePhoto(Guid orgId, [FromQuery] string url, CancellationToken ct) =>
        Ok(await organizations.DeletePhotoAsync(orgId, url, ct));

    /// <summary>"Votre profil est complet à X %".</summary>
    [HttpGet("{orgId:guid}/completion")]
    public async Task<ActionResult<ProfileCompletionDto>> Completion(Guid orgId, CancellationToken ct) =>
        Ok(await organizations.GetCompletionAsync(orgId, ct));

    /// <summary>Public link + share text + QR payload.</summary>
    [HttpGet("{orgId:guid}/public-link")]
    public async Task<ActionResult<PublicLinkDto>> PublicLink(Guid orgId, CancellationToken ct) =>
        Ok(await organizations.GetPublicLinkAsync(orgId, ct));

    [HttpGet("{orgId:guid}/closed-periods")]
    public async Task<ActionResult<IReadOnlyList<ClosedPeriodDto>>> ClosedPeriods(Guid orgId, [FromQuery] DateTime? fromUtc, CancellationToken ct) =>
        Ok(await organizations.GetClosedPeriodsAsync(orgId, fromUtc, ct));

    /// <summary>Block time: holidays, day off, break (whole business or one resource).</summary>
    [HttpPost("{orgId:guid}/closed-periods")]
    public async Task<ActionResult<ClosedPeriodDto>> AddClosedPeriod(Guid orgId, CreateClosedPeriodRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await organizations.AddClosedPeriodAsync(orgId, request, ct));

    [HttpDelete("{orgId:guid}/closed-periods/{periodId:guid}")]
    public async Task<IActionResult> DeleteClosedPeriod(Guid orgId, Guid periodId, CancellationToken ct)
    {
        await organizations.DeleteClosedPeriodAsync(orgId, periodId, ct);
        return NoContent();
    }
}
