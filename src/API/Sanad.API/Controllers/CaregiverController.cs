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
}