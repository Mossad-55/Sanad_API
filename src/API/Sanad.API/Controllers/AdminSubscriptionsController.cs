using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.SubscriptionPlanAdmin)]
[Route("api/v1/admin/subscriptions")]
public sealed class AdminSubscriptionsController : ApiControllerBase
{
    private readonly ISender _sender;

    public AdminSubscriptionsController(ISender sender) => _sender = sender;

    [HttpPost("plans")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePlan(CreateSubscriptionPlanRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId)) return Unauthorized();

        var result = await _sender.Send(new CreateSubscriptionPlanVersionCommand(
            request.Key, request.Version, request.Price, request.Cycle, request.Currency,
            request.Benefits.Select(item => new SubscriptionBenefitInput(item.Key, item.IsIncluded)).ToArray(),
            new SubscriptionLimitInput(request.MemberLimit.Kind, request.MemberLimit.Value),
            new SubscriptionLimitInput(request.MonthlyBookingLimit.Kind, request.MonthlyBookingLimit.Value),
            request.Rollover, actorUserId), cancellationToken);

        if (result.IsFailure) return PlanFailure(result.Error);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPost("plans/{planVersionId:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishPlan(Guid planVersionId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId)) return Unauthorized();
        var result = await _sender.Send(
            new PublishSubscriptionPlanVersionCommand(planVersionId, actorUserId), cancellationToken);
        return result.IsSuccess ? NoContent() : PlanFailure(result.Error);
    }

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

    [HttpPost("coupons")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCoupon(CreateSubscriptionCouponRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId)) return Unauthorized();
        var result = await _sender.Send(new CreateSubscriptionCouponCommand(
            request.Code, request.SubscriptionPlanVersionId, request.DiscountPercentage, request.ExpiresOnUtc, actorUserId), cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : CouponFailure(result.Error);
    }

    [HttpGet("coupons")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionCouponResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCoupons(CancellationToken cancellationToken)
        => Ok((await _sender.Send(new GetSubscriptionCouponsQuery(), cancellationToken)).Value);

    [HttpGet("coupons/{couponId:guid}")]
    [ProducesResponseType(typeof(SubscriptionCouponResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCoupon(Guid couponId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSubscriptionCouponQuery(couponId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : CouponFailure(result.Error);
    }

    [HttpDelete("coupons/{couponId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCoupon(Guid couponId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId actorUserId)) return Unauthorized();
        var result = await _sender.Send(new DeleteSubscriptionCouponCommand(couponId, actorUserId), cancellationToken);
        return result.IsSuccess ? NoContent() : CouponFailure(result.Error);
    }

    private IActionResult PlanFailure(Error error)
    {
        int status = error.Code == "Subscriptions.Plan.NotFound"
            ? StatusCodes.Status404NotFound
            : error.Code is "Subscriptions.Plan.DuplicateVersion" or "Subscriptions.Plan.AlreadyPublished"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "Bad Request"
            },
            Detail = error.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }

    private IActionResult CouponFailure(Error error)
    {
        int status = error.Code is "Subscriptions.Coupon.NotFound" or "Subscriptions.Coupon.PlanNotFound"
            ? StatusCodes.Status404NotFound
            : error.Code == "Subscriptions.Coupon.DuplicateCode" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails { Status = status, Title = status switch { 404 => "Not Found", 409 => "Conflict", _ => "Bad Request" }, Detail = error.Message, Instance = HttpContext.Request.Path };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}

public sealed record CreateSubscriptionPlanRequest(
    string Key,
    int Version,
    decimal Price,
    SubscriptionCycle Cycle,
    string Currency,
    IReadOnlyCollection<SubscriptionBenefitRequest> Benefits,
    SubscriptionLimitRequest MemberLimit,
    SubscriptionLimitRequest MonthlyBookingLimit,
    SubscriptionRollover Rollover);

public sealed record SubscriptionBenefitRequest(SubscriptionBenefitKey Key, bool IsIncluded);
public sealed record SubscriptionLimitRequest(SubscriptionLimitKind Kind, int? Value);
public sealed record CreateSubscriptionCouponRequest(string Code, Guid SubscriptionPlanVersionId, decimal DiscountPercentage, DateTime ExpiresOnUtc);
