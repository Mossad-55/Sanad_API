using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver")]
public sealed class CaregiverController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("profile")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status201Created)]
    public async Task<IActionResult> BootstrapProfile(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId) ||
            !TryGetCaregiverTypeFromClaims(out CaregiverType type))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BootstrapCaregiverCommand(
                    userId,
                    type),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpGet("profile")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new GetCaregiverProfileQuery(userId),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("profile/medical")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedicalProfile(
        [FromBody] UpdateMedicalProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMedicalProfileCommand(
                    userId,
                    new ProfessionalTitleId(
                        request.ProfessionalTitleId),
                    request.YearsOfExperience,
                    new SpecializationId(
                        request.SpecializationId),
                    new AcademicDegreeId(
                        request.AcademicDegreeId),
                    request.CurrentWorkplace,
                    request.Biography,
                    DateTime.UtcNow),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("profile/companion")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompanionProfile(
        [FromBody] UpdateCompanionProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCompanionProfileCommand(
                    userId,
                    request.YearsOfExperience,
                    new SpecializationId(
                        request.SpecializationId),
                    request.Biography),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("profile/address")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAddress(
        [FromBody] UpdateCaregiverAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCaregiverAddressCommand(
                    userId,
                    request.DetailedAddress),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("selections")]
    [ProducesResponseType(
    typeof(CaregiverProfileResponse),
    StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSelections(
    [FromBody] UpdateCaregiverSelectionsRequest request,
    CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCaregiverSelectionsCommand(
                    userId,
                    request.ServiceIds,
                    request.LanguageIds,
                    request.AreaIds),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("pricing/medical")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedicalPricing(
        [FromBody] UpdateMedicalPricingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMedicalPricingCommand(
                    userId,
                    request.HomeVisitPrice,
                    request.EightHourShiftPrice,
                    request.TwelveHourShiftPrice,
                    request.TwentyFourHourShiftPrice),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("pricing/companion")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompanionPricing(
        [FromBody] UpdateCompanionPricingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCompanionPricingCommand(
                    userId,
                    request.HourlyPrice,
                    request.EightHourDayPrice,
                    request.OvernightPrice),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("schedule/medical")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMedicalShedule(
        [FromBody] UpdateMedicalScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateMedicalScheduleCommand(
                    userId,
                    (request.Shifts ?? [])
                        .Select(shift =>
                            new MedicalShiftItem(
                                (DayOfWeek)shift.DayOfWeek,
                                (MedicalShiftType)shift.ShiftType))
                        .ToList(),
                    (request.HomeVisitWindows ?? [])
                        .Select(window =>
                            new MedicalHomeVisitWindowItem(
                                (DayOfWeek)window.DayOfWeek,
                                window.StartTime,
                                window.EndTime))
                        .ToList()),
                    cancellationToken);

        return ToActionResult(result);
    }

    [HttpPut("schedule/companion")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCompanionSchedule(
        [FromBody] UpdateCompanionScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new UpdateCompanionScheduleCommand(
                    userId,
                    (request.Windows ?? [])
                        .Select(window =>
                            new CompanionAvailabilityWindowItem(
                                (CompanionBookingType)window.BookingType,
                                (DayOfWeek)window.DayOfWeek,
                                window.StartTime,
                                window.EndTime))
                        .ToList()),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("availability/available")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> BecomeAvailable(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BecomeAvailableCommand(
                    userId,
                    DateOnly.FromDateTime(DateTime.UtcNow)),
                cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("availability/unavailable")]
    [ProducesResponseType(
        typeof(CaregiverProfileResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> BecomeUnavailable(
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new BecomeUnavailableCommand(userId),
                cancellationToken);

        return ToActionResult(result);
    }
}