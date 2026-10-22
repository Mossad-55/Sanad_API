using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.Rentals.Application;

namespace Sanad.API.Controllers;

public sealed record CreateRentalEquipmentRequest(
    string NameEnglish,
    string NameArabic,
    string? DescriptionEnglish,
    string? DescriptionArabic,
    long DailyRateMinorUnits,
    long DepositMinorUnits,
    int MaximumRentalDays);

public sealed record UpdateRentalEquipmentRequest(
    string NameEnglish,
    string NameArabic,
    string? DescriptionEnglish,
    string? DescriptionArabic,
    long DailyRateMinorUnits,
    long DepositMinorUnits,
    int MaximumRentalDays);

public sealed record SetRentalEquipmentActiveRequest(bool IsActive);

/// <summary>
/// The bounded rentals catalog lifecycle (clause 8, Owner decision #22: no
/// stock/inventory platform). Edits and deactivations apply only to future
/// bookings — every booking snapshots the terms displayed at booking time
/// (Owner decision #10). Rates cross the wire as exact EGP piastre integers,
/// the money-core request convention.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
[Route("api/v1/admin/rentals/catalog")]
public sealed class AdminRentalsController(ISender sender, IDateTimeProvider clock) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RentalEquipmentAdminResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        return ToActionResult(await sender.Send(new ListAdminRentalEquipmentQuery(), cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateRentalEquipmentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new CreateRentalEquipmentCommand(
                request.NameEnglish,
                request.NameArabic,
                request.DescriptionEnglish,
                request.DescriptionArabic,
                request.DailyRateMinorUnits,
                request.DepositMinorUnits,
                request.MaximumRentalDays,
                clock.UtcNow),
            cancellationToken));
    }

    [HttpPut("{equipmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid equipmentId, UpdateRentalEquipmentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new UpdateRentalEquipmentCommand(
                equipmentId,
                request.NameEnglish,
                request.NameArabic,
                request.DescriptionEnglish,
                request.DescriptionArabic,
                request.DailyRateMinorUnits,
                request.DepositMinorUnits,
                request.MaximumRentalDays,
                clock.UtcNow),
            cancellationToken));
    }

    [HttpPost("{equipmentId:guid}/activation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(Guid equipmentId, SetRentalEquipmentActiveRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _))
            return Unauthorized();

        return ToActionResult(await sender.Send(
            new SetRentalEquipmentActiveCommand(equipmentId, request.IsActive, clock.UtcNow),
            cancellationToken));
    }
}
