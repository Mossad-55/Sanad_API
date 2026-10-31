using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.Modules.CareHomes.Application.Visits;
using Ahlena.Modules.CareHomes.Domain.Bookings;

namespace Ahlena.API.Controllers;

/// <summary>
/// F6 read-only Care Home visit register for SuperAdmin/SupportAdmin, under the
/// same policy as the sibling admin Care Home bookings controller. There is no
/// Admin scheduling, approval, rejection, cancellation or override route. Rows
/// carry the facility's approved display name and the requesting family's name;
/// the projection never returns visitor name/phone (PII), visitor count or the
/// requesting family user id.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CareHomesOperationalAdmin)]
[Route("api/v1/admin/care-homes/visits")]
public sealed class AdminCareHomeVisitsController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Bounded visit list with facility, status and scheduled-window filters
    /// (inclusive UTC days). Paging is clamped (page at least 1, page size
    /// default 25, maximum 100); an empty facility id, an undefined status or an
    /// inverted window is the only rejected input. Each row carries the
    /// facility's approved display name and the family's name (both nullable).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminCareHomeVisitPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] CareHomeVisitStatus? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await sender.Send(
            new ListAdminCareHomeVisitsQuery(page, pageSize, facilityId, status, from, to),
            cancellationToken));

    /// <summary>Visit detail with the recorded decision/cancellation facts plus the nullable facility and family names; unknown id is 404.</summary>
    [HttpGet("{visitId:guid}")]
    [ProducesResponseType(typeof(AdminCareHomeVisitDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid visitId, CancellationToken cancellationToken) =>
        ToActionResult(await sender.Send(
            new GetAdminCareHomeVisitQuery(visitId),
            cancellationToken));
}
