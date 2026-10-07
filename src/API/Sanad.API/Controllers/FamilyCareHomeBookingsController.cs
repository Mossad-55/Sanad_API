using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.API.Controllers;

public sealed record CareHomeCheckoutRequest(Guid ElderlyId, Guid FacilityId, Guid RoomTypeId, DateOnly RequestedStartDate, string? CareNeedsNotes, string? ResponsibleContactName, string? ResponsibleContactPhone, string? ResponsibleContactRelationship);
public sealed record SubmitCareHomeCheckInDisputeRequest(string Reason);

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/care-home-bookings")]
public sealed class FamilyCareHomeBookingsController(ISender sender, IFamiliesDbContext families, IElderlyIntakeResolver intake, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); return ToActionResult(await sender.Send(new ListFamilyCareHomeBookingsQuery(actor, family.Id), ct)); }

    [HttpGet("{bookingId:guid}")]
    public async Task<IActionResult> Detail(Guid bookingId, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); return ToActionResult(await sender.Send(new GetFamilyCareHomeBookingQuery(actor, family.Id, bookingId, clock.UtcNow), ct)); }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(CareHomeCheckoutRequest request, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); var resolution = await intake.ResolveAsync(new ElderlyIntakeRequest(actor, family.Id, new ElderlyId(request.ElderlyId), DateOnly.FromDateTime(clock.UtcNow), request.CareNeedsNotes, request.ResponsibleContactName, request.ResponsibleContactPhone, request.ResponsibleContactRelationship), ct); if (resolution.IsFailure) return ToActionResult(resolution); return ToActionResult(await sender.Send(new CheckoutCareHomeBookingCommand(actor, family.Id, resolution.Value, request.FacilityId, request.RoomTypeId, request.RequestedStartDate, clock.UtcNow), ct)); }

    [HttpPost("{bookingId:guid}/payments/intent")]
    public async Task<IActionResult> Payment(Guid bookingId, CreatePaymentIntentRequest request, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); return ToActionResult(await sender.Send(new CreateCareHomePaymentIntentCommand(actor, family.Id, bookingId, request.Method, request.Billing, clock.UtcNow), ct)); }
    [HttpPost("{bookingId:guid}/check-in-confirmation")]
    public async Task<IActionResult> ConfirmCheckIn(Guid bookingId, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); return ToActionResult(await sender.Send(new ConfirmCareHomeCheckInCommand(actor, family.Id, bookingId, clock.UtcNow), ct)); }

    [HttpPost("{bookingId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid bookingId, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); return ToActionResult(await sender.Send(new CancelFamilyCareHomeBookingCommand(actor, family.Id, bookingId, clock.UtcNow), ct)); }

    [HttpPost("{bookingId:guid}/check-in-dispute")]
    public async Task<IActionResult> DisputeCheckIn(Guid bookingId, SubmitCareHomeCheckInDisputeRequest request, CancellationToken ct)
    { if (!TryGetAuthenticatedUserId(out UserId actor)) return Unauthorized(); var family = await families.Families.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == actor || x.Members.Any(m => m.Id == actor), ct); if (family is null) return NotFound(); return ToActionResult(await sender.Send(new SubmitCareHomeCheckInDisputeCommand(actor, family.Id, bookingId, request.Reason, clock.UtcNow), ct)); }
}
