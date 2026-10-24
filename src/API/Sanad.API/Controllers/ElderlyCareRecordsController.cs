using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.CareRecords;
using Sanad.Modules.Families.Domain.CareRecords;

namespace Sanad.API.Controllers;

/// <summary>
/// The elderly user's own combined care-record timeline across all three
/// sources. Read-only by locked scope: there is no create, edit or delete
/// route on this surface, and the dependent identity is always derived from
/// the authenticated elderly user — the route never accepts an elderly id.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.ElderlyAccess)]
[Route("api/v1/elderly/care-records")]
public sealed class ElderlyCareRecordsController : ApiControllerBase
{
    private readonly ISender _sender;

    public ElderlyCareRecordsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CareRecordsPage<CareRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? source,
        [FromQuery] DateOnly? visitDateFrom,
        [FromQuery] DateOnly? visitDateTo,
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        if (!FamilyCareRecordsController.TryParseSource(
                source, out CareRecordSource? parsedSource, out string? sourceFailureCode))
        {
            return BadRequestWithCode(sourceFailureCode!);
        }

        var result = await _sender.Send(new ListElderlyCareRecordsQuery(
            userId,
            parsedSource,
            visitDateFrom,
            visitDateTo,
            search,
            page,
            pageSize), cancellationToken);

        return ToActionResult(result);
    }
}
