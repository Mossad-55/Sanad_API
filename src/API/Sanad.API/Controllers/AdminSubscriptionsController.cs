using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.SubscriptionPlanAdmin)]
[Route("api/v1/admin/subscriptions")]
public sealed class AdminSubscriptionsController : ApiControllerBase
{
    private readonly ISender _sender;

    public AdminSubscriptionsController(ISender sender) => _sender = sender;

    [HttpPost("plans/{planVersionId:guid}/retire")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetirePlan(Guid planVersionId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId)) return Unauthorized();

        Result result = await _sender.Send(
            new RetireSubscriptionPlanCommand(planVersionId, actorUserId), cancellationToken);

        if (result.IsSuccess) return NoContent();
        int status = result.Error.Code == "Subscriptions.Plan.NotFound"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status409Conflict;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status404NotFound ? "Not Found" : "Conflict",
            Detail = result.Error.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = result.Error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
