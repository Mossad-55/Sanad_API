using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.CareRecords;

namespace Sanad.API.Controllers;

/// <summary>
/// Admin operational care-record surface, locked D4: SuperAdmin +
/// SupportAdmin list/detail READ-ONLY (existing OperationalReadAdmin policy,
/// whose membership is exactly those two roles). Every list or detail call
/// writes a payload-free audit row (actor, action, record id, timestamp,
/// correlation id) — never record content or filter text. There is no
/// create/edit/delete route and no author-impersonation capability here.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.OperationalReadAdmin)]
[Route("api/v1/admin/care-records")]
public sealed class AdminCareRecordsController : ApiControllerBase
{
    private readonly ISender _sender;

    public AdminCareRecordsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CareRecordsPage<CareRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? source,
        [FromQuery] Guid? dependentId,
        [FromQuery] DateOnly? visitDateFrom,
        [FromQuery] DateOnly? visitDateTo,
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new ListAdminCareRecordsQuery(
            userId,
            source,
            dependentId,
            visitDateFrom,
            visitDateTo,
            search,
            page,
            pageSize,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{recordId:guid}")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid recordId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new GetAdminCareRecordQuery(
            userId,
            recordId,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }
}
