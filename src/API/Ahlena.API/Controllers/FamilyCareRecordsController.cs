using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.Idempotency;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.CareRecords;
using Ahlena.Modules.Families.Domain.CareRecords;

namespace Ahlena.API.Controllers;

public sealed record CreateCareRecordRequest(string HospitalName, DateOnly VisitDate, string Details);

public sealed record UpdateCareRecordRequest(
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    int ExpectedVersion);

/// <summary>
/// Family care-record surface. Read: any family member role, all three
/// sources combined. Write: Owner/Editor only (server-derived from family
/// membership, never caller-supplied), family-source records only.
/// Routes follow the locked P0 map under the existing family dependent
/// prefix. requestIds are never trusted for attribution.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/dependents/{dependentId:guid}/care-records")]
public sealed class FamilyCareRecordsController : ApiControllerBase
{
    private readonly ISender _sender;

    public FamilyCareRecordsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(CareRecordsPage<CareRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        Guid dependentId,
        [FromQuery] string? source,
        [FromQuery] DateOnly? visitDateFrom,
        [FromQuery] DateOnly? visitDateTo,
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        if (!TryParseSource(source, out CareRecordSource? parsedSource, out string? sourceFailureCode))
        {
            return BadRequestWithCode(sourceFailureCode!);
        }

        var result = await _sender.Send(new ListFamilyCareRecordsQuery(
            userId,
            new ElderlyId(dependentId),
            parsedSource,
            visitDateFrom,
            visitDateTo,
            search,
            page,
            pageSize), cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("{recordId:guid}")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid dependentId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new GetFamilyCareRecordQuery(
            userId,
            new ElderlyId(dependentId),
            recordId), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid dependentId,
        [FromBody] CreateCareRecordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        if (!TryGetIdempotencyKey(
                "Families.CareRecords.IdempotencyKeyInvalid",
                out IdempotencyKey? key,
                out IActionResult? keyFailure))
        {
            return keyFailure!;
        }

        IdempotencyRequest? idempotency = key is null
            ? null
            : new IdempotencyRequest(
                key,
                IdempotencyFingerprint.Compute(
                    IdempotencyOperations.FamilyCareRecordCreate,
                    ("dependentId", IdempotencyFingerprint.Id(dependentId)),
                    ("hospitalName", IdempotencyFingerprint.Text(request.HospitalName)),
                    ("visitDate", IdempotencyFingerprint.Date(request.VisitDate)),
                    ("details", IdempotencyFingerprint.Text(request.Details))));

        var result = await _sender.Send(new CreateFamilyCareRecordCommand(
            userId,
            new ElderlyId(dependentId),
            request.HospitalName,
            request.VisitDate,
            request.Details,
            HttpContext.TraceIdentifier,
            idempotency), cancellationToken);

        return IdempotentCreated(result);
    }

    [HttpPut("{recordId:guid}")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid dependentId,
        Guid recordId,
        [FromBody] UpdateCareRecordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new UpdateFamilyCareRecordCommand(
            userId,
            new ElderlyId(dependentId),
            recordId,
            request.HospitalName,
            request.VisitDate,
            request.Details,
            request.ExpectedVersion,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("{recordId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid dependentId,
        Guid recordId,
        [FromQuery] int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var result = await _sender.Send(new DeleteFamilyCareRecordCommand(
            userId,
            new ElderlyId(dependentId),
            recordId,
            expectedVersion,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Parses the wire source name (Family | CareHome | Caregiver,
    /// case-insensitive). An omitted value keeps the combined timeline; a
    /// defined-but-unknown value is a 400 before any authorization runs.
    /// </summary>
    internal static bool TryParseSource(
        string? source,
        out CareRecordSource? parsed,
        out string? failureCode)
    {
        parsed = null;
        failureCode = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        if (Enum.TryParse(source.Trim(), ignoreCase: true, out CareRecordSource value)
            && Enum.IsDefined(value))
        {
            parsed = value;
            return true;
        }

        failureCode = CareRecordErrors.InvalidQuery.Code;
        return false;
    }
}
