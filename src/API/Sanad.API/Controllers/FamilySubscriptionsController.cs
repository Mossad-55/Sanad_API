using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.API.Controllers;

public sealed record CreateSubscriptionQuoteRequest(Guid PlanVersionId, string? CouponCode);
public sealed record CreateSubscriptionPaymentIntentRequest(
    Guid PlanVersionId,
    string? CouponCode,
    SubscriptionPaymentMethod Method,
    PaymobBillingData Billing);

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

    [HttpPost("quote")]
    [ProducesResponseType(typeof(SubscriptionQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Quote(
        [FromBody] CreateSubscriptionQuoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        var result = await _sender.Send(
            new CreateSubscriptionQuoteCommand(
                userId,
                request.PlanVersionId,
                request.CouponCode,
                DateTime.UtcNow),
            cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        int status = result.Error.Code switch
        {
            "Subscriptions.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Quote.PlanNotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.Quote.CouponInvalid" => StatusCodes.Status400BadRequest,
            "Subscriptions.Quote.TaxNotConfigured" => StatusCodes.Status409Conflict,
            _ => 0
        };

        return status == 0 ? ToActionResult(result) : Problem(status, result.Error);
    }

    [HttpPost("payment-intent")]
    [ProducesResponseType(typeof(SubscriptionPaymentIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreatePaymentIntent(
        [FromBody] CreateSubscriptionPaymentIntentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        var result = await _sender.Send(
            new CreateSubscriptionPaymentIntentCommand(
                userId,
                request.PlanVersionId,
                request.CouponCode,
                request.Method,
                request.Billing,
                DateTime.UtcNow),
            cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        int status = result.Error.Code switch
        {
            "Subscriptions.Payment.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Quote.PlanNotFound" or "Subscriptions.Payment.PlanNotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.Quote.CouponInvalid" or "Subscriptions.Payment.NotRequired" => StatusCodes.Status400BadRequest,
            "Subscriptions.Payment.CurrentExists" => StatusCodes.Status409Conflict,
            "Paymob.MethodNotAvailable" => StatusCodes.Status409Conflict,
            "Paymob.GatewayError" => StatusCodes.Status502BadGateway,
            "Paymob.NotConfigured" => StatusCodes.Status503ServiceUnavailable,
            _ => 0
        };

        return status == 0 ? ToActionResult(result) : Problem(status, result.Error);
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

    private IActionResult Problem(int status, Error error)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Bad Request",
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status502BadGateway => "Bad Gateway",
                StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
                _ => "Conflict"
            },
            Detail = error.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
