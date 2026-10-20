using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.Modules.Families.Application.Families;

namespace Sanad.API.Controllers;

/// <summary>
/// F6 read-only family register for SuperAdmin/SupportAdmin
/// (<see cref="AuthorizationPolicies.OperationalReadAdmin"/>). The reads never
/// project medical, contact or assessment data, expose no write route, and the
/// policy deliberately does not include FinanceAdmin, Family, Elderly, Caregiver
/// or CareHomeOwner access, so no existing Finance booking access is widened to
/// family data.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.OperationalReadAdmin)]
[Route("api/v1/admin/families")]
public sealed class AdminFamiliesController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Bounded family list. Paging is clamped (page at least 1, page size
    /// default 25, maximum 100) and <c>search</c> is never an error: a blank
    /// search and a search that matches nothing both return an empty page.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminFamilyPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new ListAdminFamiliesQuery(page, pageSize, search),
            cancellationToken));

    /// <summary>Family detail with members and dependent associations; unknown id is 404.</summary>
    [HttpGet("{familyId:guid}")]
    [ProducesResponseType(typeof(AdminFamilyDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid familyId, CancellationToken cancellationToken) =>
        ToActionResult(await sender.Send(
            new GetAdminFamilyQuery(familyId),
            cancellationToken));
}
