using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.CareRecords;

namespace Sanad.API.Controllers;

public sealed record CreateCareHomeCareRecordRequest(string HospitalName, DateOnly VisitDate, string Details);

public sealed record UpdateCareHomeCareRecordRequest(
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    int ExpectedVersion);

public sealed record CareHomeResidentResponse(Guid DependentId, string ArabicFullName, string EnglishFullName);

/// <summary>
/// Care-home dashboard surface, locked D3: the facility OWNER only (no
/// invented staff role), scoped to that facility's accepted resident
/// bookings. The facility is resolved here from the authenticated owner and
/// the resident's accepted-stay scope is rechecked server-side against the
/// CareHomes module on every action; the handler binds the record's source
/// and facility attribution itself. Created entries appear in family and
/// elderly combined reads, which share this one aggregate.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
[Route("api/v1/care-homes/residents/mine")]
public sealed class CareHomeCareRecordsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly ICareHomesDbContext _careHomesDb;
    private readonly IFamiliesDbContext _familiesDb;

    public CareHomeCareRecordsController(
        ISender sender,
        ICareHomesDbContext careHomesDb,
        IFamiliesDbContext familiesDb)
    {
        _sender = sender;
        _careHomesDb = careHomesDb;
        _familiesDb = familiesDb;
    }

    /// <summary>Accepted residents of the caller's facility: paged bilingual names only.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CareRecordsPage<CareHomeResidentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListResidents(
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        CareHomeFacility? facility = await _careHomesDb.Facilities
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.OwnerUserId == userId, cancellationToken);

        if (facility is null)
        {
            return Unauthorized();
        }

        var pageQuery = new CareRecordPageQuery(
            search, page, pageSize);

        if (pageQuery.Normalize() is not null)
        {
            return BadRequestWithCode(CareRecordErrors.InvalidQuery.Code);
        }

        // Locked scope: distinct residents with an ACCEPTED booking at this
        // facility. Names come from the Families module; the resident set
        // comes from the facility's own bookings. Note paged/search over the
        // families query with an id set that stays inside the facility.
        List<Guid> residentIds = await _careHomesDb.Bookings
            .AsNoTracking()
            .Where(b => b.FacilityId == facility.Id && b.Status == CareHomeBookingStatus.Accepted)
            .Select(b => b.ElderlyId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var elderlies = _familiesDb.Elderlies
            .AsNoTracking()
            .Where(e => residentIds.Contains(e.Id.Value));

        if (!string.IsNullOrWhiteSpace(search))
        {
            string needle = search.Trim().ToLowerInvariant();

            if (needle.Length > CareRecordPageSettings.MaxSearchLength)
            {
                return BadRequestWithCode(CareRecordErrors.InvalidQuery.Code);
            }

            elderlies = elderlies.Where(e =>
                e.ArabicFullName.Value.ToLower().Contains(needle) ||
                e.EnglishFullName.Value.ToLower().Contains(needle));
        }

        int totalCount = await elderlies.CountAsync(cancellationToken);

        List<CareHomeResidentResponse> items = (await elderlies
                .OrderBy(e => e.ArabicFullName.Value)
                .ThenBy(e => e.Id)
                .Skip((pageQuery.Page - 1) * pageQuery.PageSize)
                .Take(pageQuery.PageSize)
                .ToListAsync(cancellationToken))
            .Select(e => new CareHomeResidentResponse(e.Id.Value, e.ArabicFullName.Value, e.EnglishFullName.Value))
            .ToList();

        return Ok(CareRecordPageQuery.CreatePage(
            items, pageQuery.Page, pageQuery.PageSize, totalCount));
    }

    /// <summary>CareHome-source records of one accepted resident at the caller's facility.</summary>
    [HttpGet("{residentId:guid}/care-records")]
    [ProducesResponseType(typeof(CareRecordsPage<CareRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid residentId,
        [FromQuery] DateOnly? visitDateFrom,
        [FromQuery] DateOnly? visitDateTo,
        [FromQuery] string? search,
        [FromQuery] int page = CareRecordPageSettings.DefaultPage,
        [FromQuery] int pageSize = CareRecordPageSettings.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        CareHomeFacility? facility = await ResolveFacilityAsync(cancellationToken);

        if (facility is null)
        {
            return Unauthorized();
        }

        IActionResult? scopeFailure = await EnsureAcceptedResidentAsync(facility, residentId, cancellationToken);

        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var result = await _sender.Send(new ListCareHomeCareRecordsQuery(
            facility.Id.Value,
            new ElderlyId(residentId),
            visitDateFrom,
            visitDateTo,
            search,
            page,
            pageSize), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("{residentId:guid}/care-records")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        Guid residentId,
        [FromBody] CreateCareHomeCareRecordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        CareHomeFacility? facility = await ResolveFacilityAsync(cancellationToken);

        if (facility is null)
        {
            return Unauthorized();
        }

        IActionResult? scopeFailure = await EnsureAcceptedResidentAsync(facility, residentId, cancellationToken);

        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var result = await _sender.Send(new CreateCareHomeCareRecordCommand(
            facility.Id.Value,
            userId,
            new ElderlyId(residentId),
            request.HospitalName,
            request.VisitDate,
            request.Details,
            HttpContext.TraceIdentifier), cancellationToken);

        if (result.IsFailure) return ToActionResult(result);

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpGet("{residentId:guid}/care-records/{recordId:guid}")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid residentId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        CareHomeFacility? facility = await ResolveFacilityAsync(cancellationToken);

        if (facility is null)
        {
            return Unauthorized();
        }

        IActionResult? scopeFailure = await EnsureAcceptedResidentAsync(facility, residentId, cancellationToken);

        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var result = await _sender.Send(new GetCareHomeCareRecordQuery(
            facility.Id.Value,
            recordId), cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("{residentId:guid}/care-records/{recordId:guid}")]
    [ProducesResponseType(typeof(CareRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid residentId,
        Guid recordId,
        [FromBody] UpdateCareHomeCareRecordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        CareHomeFacility? facility = await ResolveFacilityAsync(cancellationToken);

        if (facility is null)
        {
            return Unauthorized();
        }

        IActionResult? scopeFailure = await EnsureAcceptedResidentAsync(facility, residentId, cancellationToken);

        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var result = await _sender.Send(new UpdateCareHomeCareRecordCommand(
            facility.Id.Value,
            userId,
            recordId,
            request.HospitalName,
            request.VisitDate,
            request.Details,
            request.ExpectedVersion,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }

    [HttpDelete("{residentId:guid}/care-records/{recordId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid residentId,
        Guid recordId,
        [FromQuery] int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        CareHomeFacility? facility = await ResolveFacilityAsync(cancellationToken);

        if (facility is null)
        {
            return Unauthorized();
        }

        IActionResult? scopeFailure = await EnsureAcceptedResidentAsync(facility, residentId, cancellationToken);

        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        var result = await _sender.Send(new DeleteCareHomeCareRecordCommand(
            facility.Id.Value,
            userId,
            recordId,
            expectedVersion,
            HttpContext.TraceIdentifier), cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Server-derived facility: the single facility row owned by the
    /// authenticated user. No caller-supplied facility or resident
    /// attribution is ever trusted.
    /// </summary>
    private async Task<CareHomeFacility?> ResolveFacilityAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return null;
        }

        return await _careHomesDb.Facilities
            .AsNoTracking()
            .SingleOrDefaultAsync(f => f.OwnerUserId == userId, cancellationToken);
    }

    /// <summary>
    /// The locked D3 scope predicate, rechecked on every action: the resident
    /// must be an elderly known to the Families module AND hold at least one
    /// ACCEPTED booking at this facility. A resident of another facility, or
    /// an unknown dependent, is a foreign-scope denial â€” 403 for a known
    /// other-facility dependent, 404 for an unknown one (no existence
    /// disclosure).
    /// </summary>
    private async Task<IActionResult?> EnsureAcceptedResidentAsync(
        CareHomeFacility facility,
        Guid residentId,
        CancellationToken cancellationToken)
    {
        var elderlyId = new ElderlyId(residentId);

        bool residentExists = await _familiesDb.Elderlies.AnyAsync(e => e.Id == elderlyId, cancellationToken);

        if (!residentExists)
        {
            return ToActionResult(Result.Failure(
                CareRecordErrors.DependentNotFound));
        }

        bool acceptedAtFacility = await _careHomesDb.Bookings.AnyAsync(
            b => b.FacilityId == facility.Id
                && b.ElderlyId == elderlyId
                && b.Status == CareHomeBookingStatus.Accepted,
            cancellationToken);

        if (!acceptedAtFacility)
        {
            return ToActionResult(Result.Failure(
                CareRecordErrors.AccessDenied));
        }

        return null;
    }
}
