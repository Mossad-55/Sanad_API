using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Splash;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.CmsContent)]
[Route("api/v1/admin/splash-screens")]
public sealed class AdminSplashScreensController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public AdminSplashScreensController(
        ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSplashScreenRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new CreateSplashScreenCommand(
                    request.InternalName,
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicDescription,
                    request.EnglishDescription,
                    request.ArabicButtonText,
                    request.EnglishButtonText,
                    request.ImagePath,
                    request.BackgroundColor,
                    request.DisplayOrder),
                cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(
                result);
        }

        return StatusCode(
            StatusCodes.Status201Created,
            result.Value);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSplashScreenRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new UpdateSplashScreenCommand(
                    new SplashScreenId(id),
                    request.ArabicTitle,
                    request.EnglishTitle,
                    request.ArabicDescription,
                    request.EnglishDescription,
                    request.ArabicButtonText,
                    request.EnglishButtonText,
                    request.ImagePath,
                    request.BackgroundColor,
                    request.DisplayOrder),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new PublishSplashScreenCommand(
                    new SplashScreenId(id)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType(
        typeof(SplashScreenResponse),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> Unpublish(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new UnpublishSplashScreenCommand(
                    new SplashScreenId(id)),
                cancellationToken);

        return ToActionResult(
            result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(
        StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result =
            await _sender.Send(
                new DeleteSplashScreenCommand(
                    new SplashScreenId(id)),
                cancellationToken);

        return ToActionResult(
            result);
    }
}