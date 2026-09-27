using BL.Managers;
using DTOs.Account;
using DTOs.Organizations;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers;

/// <summary>The connected user's profile, family members, favorites and notification settings.</summary>
[ApiController]
[Route("api/account")]
public sealed class AccountController(IAccountManager account) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> Me(CancellationToken ct) => Ok(await account.GetProfileAsync(ct));

    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> Update(UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await account.UpdateProfileAsync(request, ct));

    [HttpPost("me/avatar"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<UserProfileDto>> Avatar(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await account.UploadAvatarAsync(stream, file.FileName, file.ContentType, ct));
    }

    /// <summary>"Passer en mode professionnel": adds the Business role (log in again / refresh to get it in the token).</summary>
    [HttpPost("business-mode")]
    public async Task<ActionResult<UserProfileDto>> EnableBusinessMode(CancellationToken ct) => Ok(await account.EnableBusinessModeAsync(ct));

    [HttpGet("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetNotificationSettings(CancellationToken ct) =>
        Ok(await account.GetNotificationSettingsAsync(ct));

    [HttpPut("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateNotificationSettings(NotificationSettingsDto request, CancellationToken ct) =>
        Ok(await account.UpdateNotificationSettingsAsync(request, ct));

    [HttpGet("family")]
    public async Task<ActionResult<IReadOnlyList<FamilyMemberDto>>> Family(CancellationToken ct) => Ok(await account.GetFamilyAsync(ct));

    [HttpPost("family")]
    public async Task<ActionResult<FamilyMemberDto>> AddFamily(UpsertFamilyMemberRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await account.AddFamilyMemberAsync(request, ct));

    [HttpPut("family/{id:guid}")]
    public async Task<ActionResult<FamilyMemberDto>> UpdateFamily(Guid id, UpsertFamilyMemberRequest request, CancellationToken ct) =>
        Ok(await account.UpdateFamilyMemberAsync(id, request, ct));

    [HttpDelete("family/{id:guid}")]
    public async Task<IActionResult> DeleteFamily(Guid id, CancellationToken ct)
    {
        await account.DeleteFamilyMemberAsync(id, ct);
        return NoContent();
    }

    [HttpGet("favorites")]
    public async Task<ActionResult<IReadOnlyList<OrganizationCardDto>>> Favorites(CancellationToken ct) => Ok(await account.GetFavoritesAsync(ct));

    [HttpPut("favorites/{organizationId:guid}")]
    public async Task<IActionResult> AddFavorite(Guid organizationId, CancellationToken ct)
    {
        await account.AddFavoriteAsync(organizationId, ct);
        return NoContent();
    }

    [HttpDelete("favorites/{organizationId:guid}")]
    public async Task<IActionResult> RemoveFavorite(Guid organizationId, CancellationToken ct)
    {
        await account.RemoveFavoriteAsync(organizationId, ct);
        return NoContent();
    }

    /// <summary>Deletes (anonymizes) the account. Requires the password.</summary>
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken ct)
    {
        await account.DeleteAccountAsync(request, ct);
        return NoContent();
    }
}
