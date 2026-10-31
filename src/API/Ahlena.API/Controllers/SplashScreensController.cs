
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.Modules.Cms.Application.Splash;

namespace Ahlena.API.Controllers;

[Route("api/v1/splash-screens")]
public sealed class SplashScreensController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public SplashScreensController(
        ISender sender)
    {
        _sender = sender;
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(
        typeof(IReadOnlyList<SplashScreenPublicItem>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublished(
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new GetPublishedSplashScreensQuery(),
                cancellationToken);

        return ToActionResult(
            result);
    }
}