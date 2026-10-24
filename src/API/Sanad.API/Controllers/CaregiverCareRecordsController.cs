using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.CareRecords;

namespace Sanad.API.Controllers;

public sealed record CreateCaregiverCareRecordRequest(
    Guid DependentId,
    string HospitalName,
    DateOnly VisitDate,
    string Details);

public sealed record UpdateCaregiverCareRecordRequest(
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    int ExpectedVersion);

/// <summary>
/// Caregiver care-record surface. Both medical and companion caregivers are
/// covered: type changes nothing. The caregiver identity (CaregiverId) is
/// resolved here from the authenticated user, server-side, exactly as the
/// bookings controller does; dependentId on create is a conversation
/// selector, never authority â€” the booking window (Confirmed / InProgress /
/// 30-day completion grace) is revalidated inside the handler on every read
/// and write.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver/care-records")]
public sealed class CaregiverCareRecordsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly ICaregiversDbContext _caregiversDb;

    public CaregiverCareRecordsController(
        ISender sender,
        ICaregiversDbContext caregiversDb)
    {
        _sender = sender;
        _caregiversDb = caregiversDb;
    }

    /// <summary>Authorized-recipient picker: paged bilingual names only.</summary>
    [HttpGet("recipients")]
    [ProducesResponseType(typeof(CareRecordsPage<CareRecordRecipientResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListRecipients(
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var caregiver = await _caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new ListCaregiverRecipientsQuery(
            caregiver.Id,
            search,
            page,
            pageSize), cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>Own-source (Caregiver) records of one authorized dependent.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CareRecordsPage<CareRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] Guid dependentId,
        [FromQuery] DateOnly? visitDateFrom,
        [FromQuery] DateOnly? visitDateTo,
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var caregiver = await _caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new ListCaregiverCareRecordsQuery(
            caregiver.Id,
            new ElderlyId(dependentId),
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
        Guid recordId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var caregiver = await _caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new GetCaregiverCareRecordQuery(
            caregiver.Id,
            recordId), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCaregiverCareRecordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var caregiver = await _caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new CreateCaregiverCareRecordCommand(
            caregiver.Id,
            userId,
            new ElderlyId(request.DependentId),
            request.HospitalName,
            request.VisitDate,
            request.Details,
            HttpContext.TraceIdentifier), cancellationToken);

        if (result.IsFailure) return ToActionResult(result);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPut("{recordId:guid}")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid recordId,
        [FromBody] UpdateCaregiverCareRecordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var caregiver = await _caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new UpdateCaregiverCareRecordCommand(
            caregiver.Id,
            userId,
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
        Guid recordId,
        [FromQuery] int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        var caregiver = await _caregiversDb.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new DeleteCaregiverCareRecordCommand(
            caregiver.Id,
            userId,
            recordId,
            expectedVersion,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }
}
