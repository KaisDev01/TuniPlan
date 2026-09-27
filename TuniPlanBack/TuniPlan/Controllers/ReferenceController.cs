using BL.Mapping;
using Common.Helpers;
using DTOs.Common;
using Entities.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TuniPlan.Controllers;

[ApiController]
[Route("api/reference")]
[AllowAnonymous]
public sealed class ReferenceController : ControllerBase
{
    /// <summary>Categories and the 24 Tunisian governorates (for forms and filters).</summary>
    [HttpGet, ResponseCache(Duration = 3600)]
    public ActionResult<ReferenceDataDto> Get() => Ok(new ReferenceDataDto(
        Enum.GetValues<BusinessCategory>().Select(c => new OptionDto(c.ToString(), Mappers.CategoryLabel(c))).ToList(),
        TunisiaReference.Governorates));
}
