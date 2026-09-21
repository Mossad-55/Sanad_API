using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/subscriptions")]
public sealed class FamilySubscriptionsController : ApiControllerBase
{
    private readonly ISender _sender;

    public FamilySubscriptionsController(ISender sender) => _sender = sender;

    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionPlanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        return ToActionResult(await _sender.Send(new GetSubscriptionCatalogQuery(userId), cancellationToken));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(CurrentSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        return ToActionResult(await _sender.Send(new GetCurrentSubscriptionQuery(userId), cancellationToken));
    }

    [HttpPost("cancel-renewal")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelRenewal(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToSubscriptionCommandResult(await _sender.Send(new CancelSubscriptionRenewalCommand(userId), cancellationToken));
    }

    [HttpPost("reenable-auto-renew")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReenableAutoRenew(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToSubscriptionCommandResult(await _sender.Send(new ReenableSubscriptionAutoRenewCommand(userId), cancellationToken));
    }

    private IActionResult ToSubscriptionCommandResult(Result result)
    {
        if (result.IsSuccess) return NoContent();
        int status = result.Error.Code switch
        {
            "Subscriptions.Subscription.NotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.CancelRenewal.AlreadyRequested" or "Subscriptions.ReenableAutoRenew.NotCancelled" or "Subscriptions.ReenableAutoRenew.PeriodEnded" => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (status == 0) return ToActionResult(result);
        var problem = new ProblemDetails { Status = status, Title = status == 404 ? "Not Found" : "Conflict", Detail = result.Error.Message, Instance = HttpContext.Request.Path };
        problem.Extensions["code"] = result.Error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
