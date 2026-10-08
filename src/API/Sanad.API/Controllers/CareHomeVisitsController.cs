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

namespace Sanad.API.Controllers;

public sealed record CareHomeVisitRequest(Guid FacilityId, CareHomeVisitKind Kind, Guid? ElderlyId,
    string VisitorName, string VisitorPhone, int VisitorCount, DateTimeOffset StartsAt);
public sealed record CareHomeVisitRescheduleRequest(DateTimeOffset StartsAt);
public sealed record CareHomeVisitDecisionRequest(bool Approve, string? Reason);
public sealed record CareHomeVisitCancellationRequest(string? Reason);
public sealed record CareHomeVisitWindowRequest(DayOfWeek Day, TimeOnly StartTime, TimeOnly EndTime);
public sealed record CareHomeVisitClosureRequest(DateOnly Date, string? Reason);
public sealed record CareHomeVisitSettingsRequest(int ExpectedVersion, int VisitorCapacity,
    IReadOnlyList<CareHomeVisitWindowRequest> OperatingHours,
    IReadOnlyList<CareHomeVisitWindowRequest> VisitingWindows,
    IReadOnlyList<CareHomeVisitClosureRequest> Closures);

[ApiController]
[Route("api/v1/care-homes")]
public sealed class CareHomeVisitsController(ISender sender, IFamiliesDbContext families, IDateTimeProvider clock) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("facilities/{facilityId:guid}/visit-availability")]
    public Task<IActionResult> Availability(Guid facilityId, DateOnly date, int visitorCount, CancellationToken ct) =>
        Send(new GetCareHomeVisitAvailabilityQuery(facilityId, date, visitorCount, clock.UtcNow), ct);

    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpGet("~/api/v1/family/care-home-visits")]
    public async Task<IActionResult> FamilyList(CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new ListFamilyCareHomeVisitsQuery(family!.Id, clock.UtcNow), ct);
    }

    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpPost("~/api/v1/family/care-home-visits")]
    public async Task<IActionResult> FamilyCreate(CareHomeVisitRequest request, CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        if (request.Kind == CareHomeVisitKind.Resident &&
            (request.ElderlyId is not Guid elderlyId || !await families.Elderlies.AsNoTracking()
                .AnyAsync(x => x.Id == new ElderlyId(elderlyId) && x.FamilyId == family!.Id, ct))) return NotFound();
        if (request.Kind == CareHomeVisitKind.Prospective && request.ElderlyId.HasValue) return BadRequest();
        var cmd = new CreateCareHomeVisitCommand(actor, family!.Id, request.FacilityId, request.Kind,
            request.ElderlyId, request.VisitorName, request.VisitorPhone, request.VisitorCount, request.StartsAt, clock.UtcNow);
        return await Send(cmd, ct);
    }

    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpGet("~/api/v1/family/care-home-visits/{visitId:guid}")]
    public async Task<IActionResult> FamilyGet(Guid visitId, CancellationToken ct)
    {
        var (family, _) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new GetFamilyCareHomeVisitQuery(family!.Id, visitId), ct);
    }

    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpPost("~/api/v1/family/care-home-visits/{visitId:guid}/cancel")]
    public async Task<IActionResult> FamilyCancel(Guid visitId, CareHomeVisitCancellationRequest request, CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new CancelFamilyCareHomeVisitCommand(family!.Id, visitId, actor, request.Reason, clock.UtcNow), ct);
    }

    [Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
    [HttpPost("~/api/v1/family/care-home-visits/{visitId:guid}/reschedule")]
    public async Task<IActionResult> FamilyReschedule(Guid visitId, CareHomeVisitRescheduleRequest request, CancellationToken ct)
    {
        var (family, actor) = await TryFamily(ct);
        if (family is null) return NotFound();
        return await Send(new RequestCareHomeVisitRescheduleCommand(family!.Id, visitId, actor, request.StartsAt, clock.UtcNow), ct);
    }

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("visits/mine")]
    public Task<IActionResult> OwnerList(CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor)
        ? Send(new ListOwnerCareHomeVisitsQuery(actor, clock.UtcNow), ct) : Task.FromResult<IActionResult>(Unauthorized());

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("visits/mine/{visitId:guid}/decision")]
    public Task<IActionResult> OwnerDecision(Guid visitId, CareHomeVisitDecisionRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? Send(new DecideOwnerCareHomeVisitCommand(actor, visitId, request.Approve, request.Reason, clock.UtcNow), ct)
            : Task.FromResult<IActionResult>(Unauthorized());

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPost("visits/mine/{visitId:guid}/cancel")]
    public Task<IActionResult> OwnerCancel(Guid visitId, CareHomeVisitCancellationRequest request, CancellationToken ct) =>
        TryGetAuthenticatedUserId(out UserId actor)
            ? Send(new CancelOwnerCareHomeVisitCommand(actor, visitId, request.Reason, clock.UtcNow), ct)
            : Task.FromResult<IActionResult>(Unauthorized());

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpGet("facilities/mine/visit-settings")]
    public Task<IActionResult> OwnerSettings(CancellationToken ct) => TryGetAuthenticatedUserId(out UserId actor)
        ? Send(new GetOwnerCareHomeVisitSettingsQuery(actor), ct) : Task.FromResult<IActionResult>(Unauthorized());

    [Authorize(Policy = AuthorizationPolicies.CareHomeOwnerAccess)]
    [HttpPut("facilities/mine/visit-settings")]
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
