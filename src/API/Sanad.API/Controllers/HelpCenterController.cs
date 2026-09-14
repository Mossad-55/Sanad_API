using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers;

/// <summary>
/// SET-13 signed-in Help Center read surface. The audience comes from the
/// JWT only. The read is non-destructive and never fails because the CMS has
/// not been seeded: an empty FAQ list and a null support contact are valid
/// 200 responses.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.NormalAccess)]
[Route("api/v1/help-center")]
public sealed class HelpCenterController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public HelpCenterController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(HelpCenterResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHelpCenter(
        CancellationToken cancellationToken)
    {
        if (!TryGetCmsAudienceFromClaims(
                out LegalAudience audience))
        {
            return ToActionResult(
                Result.Failure(
                    ContentErrors.UnsupportedAudience));
        }

        var result =
            await _sender.Send(
                new GetHelpCenterQuery(audience),
                cancellationToken);

        return ToActionResult(result);
    }
}
