using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Support;

namespace Sanad.API.Controllers;

[Route("api/v1/support")]
public sealed class SupportController :
    ApiControllerBase
{
    private readonly ISender _sender;

    public SupportController(
        ISender sender)
    {
        _sender = sender;
    }

    [Authorize(
        Policy =
            AuthorizationPolicies.NormalAccess)]
    [HttpPost("contact")]
    [ProducesResponseType(
        typeof(SupportRequestResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitSupportRequest(
        [FromBody] SubmitSupportRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(
            out UserId userId))
        {
            return Unauthorized();
        }

        var result =
            await _sender.Send(
                new SubmitSupportRequestCommand(
                    userId,
                    request.Subject,
                    request.Message),
                cancellationToken);

        return ToActionResult(result);
    }
}
