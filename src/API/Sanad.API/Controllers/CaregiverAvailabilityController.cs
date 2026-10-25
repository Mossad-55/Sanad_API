using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Availability;

namespace Sanad.API.Controllers;

[Authorize(Policy = "FamilyAccess")]
[Route("api/v1/caregivers")]
public sealed class CaregiverAvailabilityController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverAvailabilityController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{caregiverId:guid}/availability")]
    [ProducesResponseType(
        typeof(CaregiverAvailabilityResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability(
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var query = new GetCaregiverAvailabilityQuery(caregiverId);
        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Additive date-specific read: declared availability for one date and
    /// shift type, excluding active booking overlaps, with the documented
    /// display zone and UTC intervals. The weekly action above is unchanged.
    /// The response is advisory, not a reservation.
    /// </summary>
    [HttpGet("{caregiverId:guid}/availability/day")]
    [ProducesResponseType(
        typeof(CaregiverDayAvailabilityResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDayAvailability(
        Guid caregiverId,
        [FromQuery] DateOnly? date,
        [FromQuery] string? shiftType,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out _))
        {
            return Unauthorized();
        }

        if (date is null ||
            !TryParseShiftType(shiftType, out BookingShiftType parsedShiftType))
        {
            return BadRequestWithCode(
                "Caregivers.Availability.InvalidQuery");
        }

        var query = new GetCaregiverDayAvailabilityQuery(
            caregiverId,
            date.Value,
            parsedShiftType);

        var result = await _sender.Send(query, cancellationToken);
        return ToActionResult(result);
    }

    private static bool TryParseShiftType(
        string? value,
        out BookingShiftType shiftType)
    {
        shiftType = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(
                value.Trim(),
                ignoreCase: true,
                out shiftType) &&
            Enum.IsDefined(shiftType);
    }
}