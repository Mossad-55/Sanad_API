using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Elderlies;
using Ahlena.Modules.Families.Application.MedicalAccess;

namespace Ahlena.API.Controllers;

/// <summary>
/// Grant-gated caregiver reads of a dependent's records (mobile item M5). The
/// route lives under the caregiver prefix and requires the caregiver account
/// policy; the medical-access grant is the actual gate and is rechecked on
/// every read inside the handler. This does not relax the family-scoped
/// <c>/api/v1/family/dependents/{dependentId}/medical-profile</c> read.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.CaregiverAccess)]
[Route("api/v1/caregiver/dependents")]
public sealed class CaregiverDependentsController : ApiControllerBase
{
    private readonly ISender _sender;

    public CaregiverDependentsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{dependentId:guid}/medical-profile")]
    [ProducesResponseType(typeof(ElderlyMedicalProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMedicalProfile(
        Guid dependentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new GetCaregiverDependentMedicalProfileQuery(
                userId,
                new ElderlyId(dependentId)),
            cancellationToken);

        return ToActionResult(result);
    }
}
