using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.Modules.Cms.Application.Landing;

namespace Sanad.API.Controllers;

[Route("api/v1/landing")]
public sealed class LandingController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public LandingController(
        ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpGet("sections")]
    [ProducesResponseType(
        typeof(IReadOnlyList<LandingSectionPublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSections(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetPublishedLandingSectionsQuery(),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [AllowAnonymous]
    [HttpGet("stats")]
    [ProducesResponseType(
        typeof(LandingStatsResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetLandingStatsQuery(),
                cancellationToken);

        return ToActionResult(
            result);
    }
}
