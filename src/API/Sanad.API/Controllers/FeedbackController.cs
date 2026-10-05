using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Feedback;

namespace Sanad.API.Controllers;

[Authorize]
[Route("api/v1/feedback")]
public sealed class FeedbackController : ApiControllerBase
{
    private readonly ISender _sender;

    public FeedbackController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("app-rating")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitAppRating(
        [FromBody] AppRatingFeedbackCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
        {
            return Unauthorized();
        }

        command = command with { UserId = userId.Value };

        var result = await _sender.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToActionResult(result);
        }

        return Ok(new { success = result.Value, message = "Rating submitted successfully" });
    }
}
