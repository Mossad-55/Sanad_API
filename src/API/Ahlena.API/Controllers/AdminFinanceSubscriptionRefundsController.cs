using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.Modules.Families.Application.Subscriptions;

namespace Ahlena.API.Controllers;

public sealed record RefundSubscriptionServiceShutdownRequest(
    Guid SubscriptionId,
    DateTime ShutdownOnUtc,
    string? IdempotencyKey = null,
    string? EvidenceReference = null);

/// <summary>
/// Platform-initiated subscription refunds under the shared Finance admin
/// surface. An Ahlena service shutdown refunds the <em>unused portion</em> of
/// the current paid period (clause 9 / Owner decision #6); the proration lives
/// with the purchase owner (Families) and the movement is recorded through the
/// Finance ledger.
/// <para>
/// The proration base is the amount <em>actually paid</em> on the subscription,
/// derived server-side from its succeeded payment attempts, and the award is
/// capped by the total capturable across those attempts (M4/M5) — the caller
/// supplies no money figures it could get wrong.
/// </para>
/// <para>
/// The <see cref="AuthorizationPolicies.RefundWorkflowAdmin"/> policy is the
/// SuperAdmin+SupportAdmin refund policy: a shutdown is a platform decision, not
/// a family self-service action, so it is deliberately not exposed on the family
/// routes. The caller supplies an idempotency key so a retry replays the same
/// prorated award instead of refunding the period twice (#17).
/// </para>
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RefundWorkflowAdmin)]
[Route("api/v1/admin/finance/subscription-refunds")]
public sealed class AdminFinanceSubscriptionRefundsController(ISender sender) : ApiControllerBase
{
    [HttpPost("service-shutdown")]
    [ProducesResponseType(typeof(SubscriptionRefundResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RefundServiceShutdown(
        RefundSubscriptionServiceShutdownRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var actorUserId))
            return Unauthorized();

        var result = await sender.Send(
            new RefundSubscriptionServiceShutdownCommand(
                request.SubscriptionId,
                actorUserId,
                request.ShutdownOnUtc,
                request.IdempotencyKey,
                request.EvidenceReference),
            cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        int status = result.Error.Code switch
        {
            "Subscriptions.Refund.SubscriptionNotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.Refund.NothingToRefund" => StatusCodes.Status404NotFound,
            "Subscriptions.Refund.NotCurrent" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.ExceedsCaptured" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.CaptureNotResolved" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.IdempotencyConflict" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.FinanceRejected" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.MalformedInput" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "Bad Request"
            },
            Detail = result.Error.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = result.Error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
