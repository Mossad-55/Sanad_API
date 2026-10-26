using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Finance.Application;

namespace Sanad.API.Controllers;

public sealed record CreateSubscriptionQuoteRequest(Guid PlanVersionId, string? CouponCode);
public sealed record CreateSubscriptionPaymentIntentRequest(
    Guid PlanVersionId,
    string? CouponCode,
    SubscriptionPaymentMethod Method,
    PaymobBillingData Billing);
public sealed record CreateSubscriptionRenewalPaymentIntentRequest(
    SubscriptionPaymentMethod Method,
    PaymobBillingData Billing);
public sealed record CreatePlanChangeQuoteRequest(Guid PlanVersionId);
public sealed record CreatePlanChangePaymentIntentRequest(Guid PlanVersionId, SubscriptionPaymentMethod Method, PaymobBillingData Billing);
public sealed record ReplacePendingDowngradeRequest(Guid PlanVersionId);
/// <summary>
/// Clause 9: an optional M7 idempotency key. When supplied, the cancel-renewal
/// outcome (retained access, zero refund) is recorded through shared Finance so
/// it is attributable and distinct from a duplicate-charge refund and a
/// shutdown proration; a retry replays that same recorded outcome.
/// </summary>
public sealed record CancelSubscriptionRenewalRequest(string? IdempotencyKey = null);
public sealed record RefundDuplicateSubscriptionChargeRequest(
    Guid PaymentAttemptId,
    string? IdempotencyKey = null,
    string? EvidenceReference = null,
    // Optional CUSTOMER-REPORTED instant of the reported duplicate observation:
    // audit-only context the family supplies, never a support decision time.
    // When omitted the handler stamps the server's current UTC time; when
    // supplied it must be UTC (a non-UTC value is a clean 400). Decision,
    // execution and reconciliation instants are server-authoritative on the
    // admin surface and are never accepted from the family.
    DateTime? RecordedOnUtc = null,
    // Support-approval migration opt-in: the explicit switch that moves this
    // address from the retired direct auto-award onto claim submission. The
    // flag is fail-closed — an absent or false value is the documented 400
    // (Subscriptions.Refund.MigratedToClaim), never a silent award.
    bool ClaimSubmission = false);

[Authorize(Policy = AuthorizationPolicies.FamilyAccess)]
[Route("api/v1/family/subscriptions")]
public sealed class FamilySubscriptionsController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly IFileStorage _fileStorage;

    public FamilySubscriptionsController(ISender sender, IFileStorage fileStorage) { _sender = sender; _fileStorage = fileStorage; }

    [HttpGet("invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionInvoiceListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvoices(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(new GetFamilySubscriptionInvoicesQuery(userId), cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return Problem(result.Error.Code == "Subscriptions.Invoice.NotOwner" ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest, result.Error);
    }

    [HttpGet("invoices/{invoiceId:guid}")]
    [ProducesResponseType(typeof(SubscriptionInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(new GetFamilySubscriptionInvoiceQuery(userId, invoiceId), cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return Problem(result.Error.Code == "Subscriptions.Invoice.NotOwner" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound, result.Error);
    }

    [HttpGet("invoices/{invoiceId:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadInvoice(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(new GetFamilySubscriptionInvoiceQuery(userId, invoiceId), cancellationToken);
        if (result.IsFailure) return Problem(result.Error.Code == "Subscriptions.Invoice.NotOwner" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound, result.Error);
        var storageKey = await _sender.Send(new GetInvoiceStorageKeyQuery(userId, invoiceId), cancellationToken);
        if (storageKey.IsFailure) return Problem(storageKey.Error.Code == "Subscriptions.Invoice.NotOwner" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound, storageKey.Error);
        var file = await _fileStorage.OpenReadAsync(storageKey.Value, cancellationToken);
        if (file.IsFailure) return NotFound();
        return File(file.Value.Content, "application/pdf", result.Value.InvoiceNumber + ".pdf");
    }

    /// <summary>
    /// Owner-only payment history. Items carry <c>amount</c> in EGP major units
    /// (not piastres), <c>paidOnUtc</c> from the provider settlement instant
    /// only, and the linked invoice id when one exists. Pending/failed attempts
    /// appear with a null <c>paidOnUtc</c>. Order is paidOnUtc descending with
    /// unpaid attempts last, then createdOnUtc and id descending.
    /// </summary>
    [HttpGet("payments")]
    [ProducesResponseType(typeof(SubscriptionPaymentPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPayments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] SubscriptionPaymentAttemptStatus? status = null,
        [FromQuery] SubscriptionPaymentMethod? method = null,
        [FromQuery] DateTime? paidFromUtc = null,
        [FromQuery] DateTime? paidToUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(
            new GetFamilySubscriptionPaymentsQuery(
                userId, page, pageSize, status, method, paidFromUtc, paidToUtc),
            cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        int statusCode = result.Error.Code switch
        {
            "Subscriptions.Payment.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Payment.InvalidQuery" => StatusCodes.Status400BadRequest,
            _ => 0
        };
        return statusCode == 0 ? ToActionResult(result) : Problem(statusCode, result.Error);
    }

    [HttpGet("payments/{paymentAttemptId:guid}")]
    [ProducesResponseType(typeof(SubscriptionPaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(Guid paymentAttemptId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(
            new GetFamilySubscriptionPaymentQuery(userId, paymentAttemptId),
            cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        int status = result.Error.Code switch
        {
            "Subscriptions.Payment.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Payment.NotFound" => StatusCodes.Status404NotFound,
            _ => 0
        };
        return status == 0 ? ToActionResult(result) : Problem(status, result.Error);
    }

    /// <summary>
    /// The published plan catalog for the family owner. Every item carries the
    /// **version-owned authored copy** (`arabicName`, `englishName`,
    /// `arabicDescription`, `englishDescription`) and the per-benefit
    /// `arabicLabel` / `englishLabel`. A copy the admin did not author is
    /// absent (<c>null</c>) — it is never generated or translated.
    /// </summary>
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

            // E04: maintenance/control failures are documented platform
            // errors, never folded into the subscription fallback.
            "Platform.Maintenance" or "Platform.ControlStateUnavailable" => StatusCodes.Status503ServiceUnavailable,
            "Platform.PaymentMethodUnavailable" => StatusCodes.Status400BadRequest,
            _ => 0
        };

        return status == 0 ? ToActionResult(result) : Problem(status, result.Error);
    }

    [HttpPost("renewal/payment-intent")]
    [ProducesResponseType(typeof(SubscriptionPaymentIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateRenewalPaymentIntent(
        [FromBody] CreateSubscriptionRenewalPaymentIntentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId))
            return Unauthorized();

        var result = await _sender.Send(
            new CreateSubscriptionRenewalPaymentIntentCommand(
                userId,
                request.Method,
                request.Billing,
                DateTime.UtcNow),
            cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        int status = result.Error.Code switch
        {
            "Subscriptions.Renewal.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Renewal.NotFound" or "Subscriptions.Renewal.PlanNotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.Renewal.NotDue" or "Subscriptions.Renewal.GraceExpired" or "Subscriptions.Renewal.PendingExists" => StatusCodes.Status409Conflict,
            "Paymob.MethodNotAvailable" => StatusCodes.Status409Conflict,
            "Paymob.GatewayError" => StatusCodes.Status502BadGateway,
            "Paymob.NotConfigured" => StatusCodes.Status503ServiceUnavailable,

            // E04: maintenance/control failures stay documented platform errors.
            "Platform.Maintenance" or "Platform.ControlStateUnavailable" => StatusCodes.Status503ServiceUnavailable,
            "Platform.PaymentMethodUnavailable" => StatusCodes.Status400BadRequest,
            _ => 0
        };

        return status == 0 ? ToActionResult(result) : Problem(status, result.Error);
    }

    [HttpPost("plan-change/quote")]
    public async Task<IActionResult> PlanChangeQuote([FromBody] CreatePlanChangeQuoteRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(new CreatePlanChangeQuoteCommand(userId, request.PlanVersionId, DateTime.UtcNow), cancellationToken);
        return ToPlanChangeResult(result);
    }

    [HttpPost("plan-change/payment-intent")]
    [ProducesResponseType(typeof(SubscriptionPaymentIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreatePlanChangePaymentIntent([FromBody] CreatePlanChangePaymentIntentRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        var result = await _sender.Send(new CreatePlanChangePaymentIntentCommand(userId, request.PlanVersionId, request.Method, request.Billing, DateTime.UtcNow), cancellationToken);
        return ToPlanChangeResult(result);
    }

    [HttpPut("pending-downgrade")]
    public async Task<IActionResult> ReplacePendingDowngrade([FromBody] ReplacePendingDowngradeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToPlanChangeCommandResult(await _sender.Send(new ReplacePendingDowngradeCommand(userId, request.PlanVersionId, DateTime.UtcNow), cancellationToken));
    }

    [HttpDelete("pending-downgrade")]
    public async Task<IActionResult> DeletePendingDowngrade(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToPlanChangeCommandResult(await _sender.Send(new CancelPendingDowngradeCommand(userId, DateTime.UtcNow), cancellationToken));
    }

    [HttpPost("cancel-renewal")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelRenewal(
        [FromBody] CancelSubscriptionRenewalRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        return ToSubscriptionCommandResult(await _sender.Send(
            new CancelSubscriptionRenewalCommand(userId, request?.IdempotencyKey), cancellationToken));
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

    /// <summary>
    /// Clause 9: a duplicate or erroneous subscription charge is refunded
    /// <em>in full</em> (never prorated) — but only after a Support verdict.
    /// This retained address is the explicit migration path: claim submission
    /// requires the <c>claimSubmission: true</c> opt-in, and the retired
    /// direct auto-award refuses everything else fail-closed. Submission never
    /// pays; the created claim is decided and executed by SuperAdmin/
    /// SupportAdmin surfaces. Replays (same idempotency key, same shape) return
    /// 200 with the original claim; a fresh claim is 201.
    /// </summary>
    [HttpPost("refunds/duplicate-charge")]
    [ProducesResponseType(typeof(FamilyDuplicateChargeClaimView), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(FamilyDuplicateChargeClaimView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RefundDuplicateCharge(
        [FromBody] RefundDuplicateSubscriptionChargeRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();

        if (!request.ClaimSubmission)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                DuplicateChargeClaimSubmissionErrors.MigratedToClaim);
        }

        Result<FamilyDuplicateChargeClaimView> result = await _sender.Send(
            new SubmitDuplicateChargeClaimCommand(
                userId,
                request.PaymentAttemptId,
                request.IdempotencyKey,
                request.EvidenceReference,
                request.RecordedOnUtc),
            cancellationToken);
        return ToDuplicateChargeClaimResult(result);
    }

    /// <summary>
    /// The family's own duplicate-charge claims, newest first. Deliberately
    /// restricted to the customer-facing projection: coarse claim/payout state
    /// and the customer-facing reason — never the verified basis, internal
    /// notes, evidence, actor ids, provider references or attempt ids.
    /// </summary>
    [HttpGet("refunds/duplicate-charge")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilyDuplicateChargeClaimView>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListDuplicateChargeClaims(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        Result<IReadOnlyList<FamilyDuplicateChargeClaimView>> result = await _sender.Send(
            new ListMyDuplicateChargeClaimsQuery(userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    /// <summary>
    /// One of the family's own claims; a foreign or unknown id is the same
    /// probe-safe 404.
    /// </summary>
    [HttpGet("refunds/duplicate-charge/{claimId:guid}")]
    [ProducesResponseType(typeof(FamilyDuplicateChargeClaimView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDuplicateChargeClaim(
        Guid claimId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out UserId userId)) return Unauthorized();
        Result<FamilyDuplicateChargeClaimView> result = await _sender.Send(
            new GetMyDuplicateChargeClaimQuery(userId, claimId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ClaimProblem(result.Error);
    }

    private IActionResult ToDuplicateChargeClaimResult(Result<FamilyDuplicateChargeClaimView> result)
    {
        if (result.IsSuccess)
        {
            return result.Value.IsReplay
                ? Ok(result.Value)
                : CreatedAtAction(nameof(GetDuplicateChargeClaim), new { claimId = result.Value.ClaimId }, result.Value);
        }
        return ClaimProblem(result.Error);
    }

    private IActionResult ClaimProblem(Error error)
    {
        int status = error.Code switch
        {
            "Subscriptions.Refund.MigratedToClaim" or "Subscriptions.Refund.MalformedInput" =>
                StatusCodes.Status400BadRequest,
            "Subscriptions.Refund.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Refund.NothingToRefund" or "Subscriptions.Refund.ClaimNotFound" =>
                StatusCodes.Status404NotFound,
            "Subscriptions.Refund.IdempotencyConflict" or
                "Subscriptions.Refund.CaptureAlreadyClaimed" or
                "Subscriptions.Refund.ExceedsCaptured" or
                "Subscriptions.Refund.FinanceRejected" or
                "Subscriptions.Refund.LegacyRetired" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status409Conflict
        };
        return Problem(status, error);
    }

    private IActionResult ToSubscriptionRefundResult(Result<SubscriptionRefundResponse> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        int status = result.Error.Code switch
        {
            "Subscriptions.Refund.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.Refund.SubscriptionNotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.Refund.NothingToRefund" => StatusCodes.Status404NotFound,
            "Subscriptions.Refund.NotCurrent" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.ExceedsCaptured" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.CaptureNotResolved" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.IdempotencyConflict" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.FinanceRejected" => StatusCodes.Status409Conflict,
            "Subscriptions.Refund.MalformedInput" => StatusCodes.Status400BadRequest,
            _ => 0
        };
        return status == 0 ? ToActionResult(result) : Problem(status, result.Error);
    }

    private IActionResult ToSubscriptionCommandResult(Result result)
    {
        if (result.IsSuccess) return NoContent();
        int status = result.Error.Code switch
        {
            "Subscriptions.Subscription.NotFound" => StatusCodes.Status404NotFound,
            "Subscriptions.CancelRenewal.AlreadyRequested" or "Subscriptions.ReenableAutoRenew.NotCancelled" or "Subscriptions.ReenableAutoRenew.PeriodEnded" or "Subscriptions.PlanChange.NotOwner" => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (status == 0) return ToActionResult(result);
        var problem = new ProblemDetails { Status = status, Title = status == 404 ? "Not Found" : "Conflict", Detail = result.Error.Message, Instance = HttpContext.Request.Path };
        problem.Extensions["code"] = result.Error.Code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }

    private IActionResult ToPlanChangeResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        int status = result.Error.Code switch
        {
            "Subscriptions.PlanChange.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.PlanChange.NotFound" or "Subscriptions.PlanChange.PlanNotFound" => StatusCodes.Status404NotFound,
            "Paymob.GatewayError" => StatusCodes.Status502BadGateway,
            "Paymob.NotConfigured" => StatusCodes.Status503ServiceUnavailable,
            "Subscriptions.PlanChange.PaymentNotRequired" => StatusCodes.Status400BadRequest,

            // Mobile integration 2026-10-26: the no-plan-change-during-paid-period
            // policy and its duplicate-attempt guard are documented 409 conflicts
            // (same outcome as the helper's own fallback, stated explicitly).
            "Subscriptions.PlanChange.PaidPeriodActive" or "Subscriptions.PlanChange.PaymentPending" =>
                StatusCodes.Status409Conflict,
            "Subscriptions.PlanChange.NotUpgrade" or "Subscriptions.PlanChange.CancelRenewalRequested" or
                "Subscriptions.PlanChange.RenewalGrace" or "Subscriptions.PendingDowngrade.NotDowngrade" =>
                StatusCodes.Status409Conflict,

            // E04: the helper's 409 fallback must never swallow the platform
            // maintenance/method outcomes.
            "Platform.Maintenance" or "Platform.ControlStateUnavailable" => StatusCodes.Status503ServiceUnavailable,
            "Platform.PaymentMethodUnavailable" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        return Problem(status, result.Error);
    }

    private IActionResult ToPlanChangeCommandResult(Result result)
    {
        if (result.IsSuccess) return NoContent();
        int status = result.Error.Code switch
        {
            "Subscriptions.PlanChange.NotOwner" => StatusCodes.Status403Forbidden,
            "Subscriptions.PlanChange.NotFound" or "Subscriptions.PlanChange.PlanNotFound" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };
        return Problem(status, result.Error);
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
