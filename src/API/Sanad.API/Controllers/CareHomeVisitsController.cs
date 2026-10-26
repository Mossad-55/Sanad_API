using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Data;
using System.ComponentModel.DataAnnotations;

namespace Sanad.API.Controllers;

public sealed record CareHomeVisitRequest(Guid FacilityId, CareHomeVisitKind Kind, Guid? ElderlyId,
    string VisitorName, string VisitorPhone, int VisitorCount, DateTimeOffset StartsAt);
public sealed record CareHomeVisitRescheduleRequest(DateTimeOffset StartsAt);
// Schema policy: only RAW-JSON-measured domain caps are published as
// request-schema annotations. The visit decision reason (consume path:
// `CareHomeVisit.Reject`, approve ignores the reason) and the cancellation
// reason (`CareHomeVisit.Cancel`) are capped at 1000 on the TRIMMED text and
// the support reply body at 1–2000 on the TRIMMED text, so a raw
// `[StringLength]` would falsely reject previously accepted whitespace-padded
// and approve-ignored values — those caps stay documented, not annotated.
// The closure reason is measured on the raw value before trimming
// (`CareHomeVisitSettings.Validate`), so its annotation is exact. Record
// positional attributes require the `property:` target modifier to reach the
// generated property schema (and MVC validation); without it the attribute
// silently stays on the constructor parameter and no maxLength is emitted.
public sealed record CareHomeVisitDecisionRequest(bool Approve, string? Reason);
public sealed record CareHomeVisitCancellationRequest(string? Reason);
public sealed record CareHomeVisitWindowRequest(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime);
public sealed record CareHomeVisitClosureRequest(DateOnly Date, [property: StringLength(500)] string? Reason);
public sealed record CareHomeVisitSettingsRequest(int ExpectedVersion, int VisitorCapacity,
    IReadOnlyList<CareHomeVisitWindowRequest> OperatingHours,
    IReadOnlyList<CareHomeVisitWindowRequest> VisitingWindows,
    IReadOnlyList<CareHomeVisitClosureRequest> Closures);

[ApiController]
[Route("api/v1/care-homes")]
public sealed class CareHomeVisitsController(ISender sender, IFamiliesDbContext families, IDateTimeProvider clock) : ApiControllerBase
{
    /// <summary>
    /// Public one-hour slot list for one Cairo-local date. A malformed facility
    /// id, date or a visitor count below 1 (or above the facility capacity) is
    /// <c>400 CareHomes.Visits.InvalidRequest</c>; an unknown or not-publicly
    /// eligible facility is <c>404 CareHomes.Discovery.NotFound</c>. An
    /// eligible facility without saved settings answers 200 with empty slots.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("facilities/{facilityId:guid}/visit-availability")]
    [ProducesResponseType(typeof(CareHomeVisitAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Availability(Guid facilityId, DateOnly date, int visitorCount, CancellationToken ct) =>
        Send(new GetCareHomeVisitAvailabilityQuery(facilityId, date, visitorCount, clock.UtcNow), ct);

    /// <summary>The caller's Family visits, newest start first. A caller with no active Family is an empty-body 404.</summary>
    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpGet("~/api/v1/family/care-home-visits")]
    [ProducesResponseType(typeof(IReadOnlyList<CareHomeVisitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FamilyList(CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new ListFamilyCareHomeVisitsQuery(family!.Id, clock.UtcNow), ct);
    }

    /// <summary>
    /// Requests a visit. Successes answer 200 with the stored visit (a
    /// supplied <c>Idempotency-Key</c> replay also answers 200). Handler
    /// rejections are ProblemDetails: a malformed create (bad slot,
    /// insufficient notice, over-capacity count, over-long key) is
    /// <c>400 CareHomes.Visits.InvalidRequest</c>, an unknown/ineligible
    /// facility is 404 (<c>CareHomes.Discovery.NotFound</c> /
    /// <c>CareHomes.Visits.NotFound</c>), and a raced key or full-hour
    /// capacity is 409 (<c>CareHomes.Visits.IdempotencyConflict</c> /
    /// <c>CareHomes.Visits.CapacityConflict</c>). Controller-side guards use
    /// the same status codes without a body: a caller with no active Family
    /// or a Resident request without an in-Family <c>elderlyId</c> is a
    /// bodyless 404, and a Prospective request with an <c>elderlyId</c> is a
    /// bodyless 400.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpPost("~/api/v1/family/care-home-visits")]
    [ProducesResponseType(typeof(CareHomeVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FamilyCreate(CareHomeVisitRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();

        // Optional retry header: absent, blank, or whitespace means today's
        // legacy create with no dedupe. A supplied key is trimmed and bounded
        // by the same house convention the domain enforces below it.
        string? retryKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (retryKey is { Length: > CareHomeVisit.MaximumIdempotencyKeyLength })
            return BadRequestWithCode(CareHomeVisitErrors.InvalidRequest.Code);

        if (request.Kind == CareHomeVisitKind.Resident &&
            (request.ElderlyId is not Guid elderlyId || !await families.Elderlies.AsNoTracking()
                .AnyAsync(x => x.Id == new ElderlyId(elderlyId) && x.FamilyId == family!.Id, ct))) return NotFound();
        if (request.Kind == CareHomeVisitKind.Prospective && request.ElderlyId.HasValue) return BadRequest();
        var cmd = new CreateCareHomeVisitCommand(actor, family!.Id, request.FacilityId, request.Kind,
            request.ElderlyId, request.VisitorName, request.VisitorPhone, request.VisitorCount, request.StartsAt, clock.UtcNow,
            retryKey);
        return await Send(cmd, ct);
    }

    /// <summary>
    /// Residents of the caller's Family eligible for a resident visit at the
    /// facility on the requested date, one deterministic row per resident.
    /// A missing/malformed facility or a default <c>startsAt</c> is
    /// <c>400 CareHomes.Visits.InvalidRequest</c>; a caller with no active
    /// Family is an empty-body 404.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpGet("~/api/v1/family/care-home-visits/eligible-residents")]
    [ProducesResponseType(typeof(IReadOnlyList<CareHomeEligibleResidentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FamilyEligibleResidents(
        [FromQuery] Guid facilityId, [FromQuery] DateTimeOffset startsAt, CancellationToken ct)
    {
        var (family, _) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new GetEligibleCareHomeVisitResidentsQuery(family!.Id, facilityId, startsAt), ct);
    }

    /// <summary>One owned visit; a foreign or unknown visit id is a 404 that never confirms another Family's visit, and a caller with no active Family is an empty-body 404.</summary>
    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpGet("~/api/v1/family/care-home-visits/{visitId:guid}")]
    [ProducesResponseType(typeof(CareHomeVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FamilyGet(Guid visitId, CancellationToken ct)
    {
        var (family, _) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new GetFamilyCareHomeVisitQuery(family!.Id, visitId), ct);
    }

    /// <summary>
    /// Cancels an owned visit before it starts and answers the updated visit;
    /// also cancels its pending reschedule. Unknown/foreign visit is 404, a
    /// visit no longer cancellable is <c>409 CareHomes.Visits.InvalidState</c>,
    /// and a caller with no active Family is an empty-body 404.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpPost("~/api/v1/family/care-home-visits/{visitId:guid}/cancel")]
    [ProducesResponseType(typeof(CareHomeVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FamilyCancel(Guid visitId, CareHomeVisitCancellationRequest request, CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new CancelFamilyCareHomeVisitCommand(family!.Id, visitId, actor, request.Reason, clock.UtcNow), ct);
    }

    /// <summary>
    /// Requests a reschedule; the approved original stays active until the
    /// replacement is decided. A disallowed slot or insufficient notice is
    /// <c>400 CareHomes.Visits.InvalidRequest</c>, an unknown visit 404, an
    /// already-pending reschedule or a no-longer-reschedulable visit 409, a
    /// lost capacity race <c>409 CareHomes.Visits.CapacityConflict</c>, and a
    /// caller with no active Family is an empty-body 404.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpPost("~/api/v1/family/care-home-visits/{visitId:guid}/reschedule")]
    [ProducesResponseType(typeof(CareHomeVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> FamilyReschedule(Guid visitId, CareHomeVisitRescheduleRequest request, CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new RequestCareHomeVisitRescheduleCommand(family!.Id, visitId, actor, request.StartsAt, clock.UtcNow), ct);
    }

    /// <summary>The owner's facility visits in start order; an owner with no facility is a 404.</summary>
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("visits/mine")]
    [ProducesResponseType(typeof(IReadOnlyList<CareHomeVisitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> OwnerList(CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor)
        ? Send(new ListOwnerCareHomeVisitsQuery(actor, clock.UtcNow), ct) : Task.FromResult<IActionResult>(Unauthorized());

    /// <summary>
    /// Approves or rejects a pending visit. Rejection requires a nonblank
    /// reason. Approving a reschedule retires the approved original in the
    /// same transaction. Unknown/foreign visit is 404; a visit not in a
    /// decidable state, an expired original or a capacity race is 409
    /// (<c>CareHomes.Visits.InvalidState</c>/<c>CareHomes.Visits.Conflict</c>).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("visits/mine/{visitId:guid}/decision")]
    [ProducesResponseType(typeof(CareHomeVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> OwnerDecision(Guid visitId, CareHomeVisitDecisionRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? Send(new DecideOwnerCareHomeVisitCommand(actor, visitId, request.Approve, request.Reason, clock.UtcNow), ct)
            : Task.FromResult<IActionResult>(Unauthorized());

    /// <summary>Cancels an owned visit before it starts and answers the updated visit; also cancels its pending reschedule. Unknown/foreign visit is 404 and a no-longer-cancellable visit is 409.</summary>
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("visits/mine/{visitId:guid}/cancel")]
    [ProducesResponseType(typeof(CareHomeVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> OwnerCancel(Guid visitId, CareHomeVisitCancellationRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? Send(new CancelOwnerCareHomeVisitCommand(actor, visitId, request.Reason, clock.UtcNow), ct)
            : Task.FromResult<IActionResult>(Unauthorized());

    /// <summary>Reads the facility's versioned visit settings; before the first save this is 200 with version 0 and capacity 0. An owner with no facility is a 404.</summary>
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("facilities/mine/visit-settings")]
    [ProducesResponseType(typeof(CareHomeVisitSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> OwnerSettings(CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor)
        ? Send(new GetOwnerCareHomeVisitSettingsQuery(actor), ct) : Task.FromResult<IActionResult>(Unauthorized());

    /// <summary>
    /// Replaces the schedule settings under <c>expectedVersion</c>. A stale
    /// version, a change that invalidates a reserved future appointment or a
    /// capacity regression is <c>409 CareHomes.Visits.Conflict</c>; invalid
    /// windows/capacity is <c>400 CareHomes.Visits.InvalidRequest</c>; a
    /// pending-review or suspended facility is
    /// <c>409 CareHomes.Visits.InvalidState</c>; an owner with no facility is
    /// a 404. Successful writes return the new settings and version.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPut("facilities/mine/visit-settings")]
    [ProducesResponseType(typeof(CareHomeVisitSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UpdateOwnerSettings(CareHomeVisitSettingsRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return Task.FromResult<IActionResult>(Unauthorized());
        return Send(new UpdateOwnerCareHomeVisitSettingsCommand(actor, request.ExpectedVersion, request.VisitorCapacity,
            request.OperatingHours.Select(x => new CareHomeVisitTimeWindow(x.Day, x.StartTime, x.EndTime)).ToArray(),
            request.VisitingWindows.Select(x => new CareHomeVisitTimeWindow(x.Day, x.StartTime, x.EndTime)).ToArray(),
            request.Closures.Select(x => new CareHomeVisitClosure(x.Date, x.Reason)).ToArray(), clock.UtcNow), ct);
    }

    private async Task<(Sanad.Modules.Families.Domain.Families.Family? Family, UserId Actor)> TryFamily(CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out UserId actor)) return (null, default);
        var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.DeletedOnUtc == null &&
            (x.OwnerUserId == actor || x.Members.Any(member => member.Id == actor)), ct);
        return (family, actor);
    }

    private async Task<IActionResult> Send<T>(MediatR.IRequest<Sanad.BuildingBlocks.Application.Results.Result<T>> request, CancellationToken ct) =>
        ToActionResult(await sender.Send(request, ct));
}
