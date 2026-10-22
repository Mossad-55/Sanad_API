using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record CancelFamilyCareHomeBookingCommand(UserId Actor, FamilyId FamilyId, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record CancelOwnerCareHomeBookingCommand(UserId Actor, Guid BookingId, string Reason, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record RetryCareHomeRefundCommand(UserId Actor, Guid BookingId, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record RecordCareHomeRefundCompletedCommand(UserId Actor, Guid BookingId, string Reference, string Reason, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;
public sealed record CompleteCareHomeRefundCallbackCommand(long TransactionId, long? ParentTransactionId, bool IsRefunded, DateTime UtcNow) : ICommand;

public static class CareHomeRefundProcessor
{
    /// <summary>
    /// The gateway client's refund outcome classes, derived from the client's
    /// actual contract (<see cref="IPaymobClient.RefundPaymentAsync"/>) and
    /// deliberately exhaustive with a conservative default:
    /// <list type="bullet">
    /// <item><description><c>Paymob.NotConfigured</c> fails the client's
    /// pre-send guard, so <b>nothing was sent</b> — definitive proof no money
    /// moved and the claim is released into a retryable failure.</description></item>
    /// <item><description><c>Paymob.RefundRejected</c> is the provider's own
    /// 4xx refusal of the refund request — the request arrived and was
    /// explicitly refused, so no refund was executed and the claim is released
    /// into a retryable failure.</description></item>
    /// <item><description><c>Paymob.RefundOutcomeUnknown</c> (a 5xx whose
    /// response may have followed a successful refund, or a transport/parse
    /// failure after the request was sent) leaves the money state unknown.</description></item>
    /// <item><description>Any other unrecognised code — including a call that
    /// was cancelled or timed out before its outcome could be read, which the
    /// client does not fold into its result — is treated as unknown, never as
    /// a refusal: only provider meanings proven above may release a claim.</description></item>
    /// </list>
    /// </summary>
    public enum RefundFailureClass
    {
        /// <summary>Proven definitive refusal; no money moved; retry is safe.</summary>
        DefinitiveNoMovement,

        /// <summary>The money state is unknown; the attempt must be marked ambiguous.</summary>
        Uncertain
    }

    /// <summary>Classifies a gateway failure without guessing about the money.</summary>
    public static RefundFailureClass ClassifyFailure(Error error) => error.Code switch
    {
        "Paymob.NotConfigured" or "Paymob.RefundRejected" => RefundFailureClass.DefinitiveNoMovement,
        _ => RefundFailureClass.Uncertain
    };

    public static async Task<Result> InitiateAsync(
        ICareHomesDbContext db,
        IPaymobClient paymob,
        CareHomeBooking booking,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (!booking.TryClaimRefund(utcNow))
            return Result.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "The refund is not ready to be submitted."));

        // The claim (with its attempt identity) is persisted before reaching
        // the provider, so the attempt is exclusive and survives a crash; the
        // optimistic version token makes a competing claim lose its commit.
        await db.SaveChangesAsync(cancellationToken);
        if (booking.PaymobTransactionId is null)
        {
            // Definitive: no provider call was possible, so nothing could have
            // moved; the claim is released into an ordinary retryable failure.
            booking.MarkRefundFailed("No Paymob transaction is stored for this paid booking.", utcNow);
            CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.RefundFailed, booking.Id, utcNow);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "The paid booking has no stored Paymob transaction."));
        }

        Result<string?> result;
        try
        {
            result = await paymob.RefundPaymentAsync(
                booking.PaymobTransactionId.Value.ToString(),
                booking.RefundAmount!.Value,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The client does not fold cancellation or its HTTP timeout into
            // its result: a cancelled call may already have reached the
            // provider and moved the money, so — by the same rule as
            // <c>Paymob.RefundOutcomeUnknown</c> — the outcome is uncertain.
            // It is never retried blindly; the durable ambiguity marker takes
            // over (the claim and its attempt identity fold into it).
            booking.MarkRefundOutcomeAmbiguous("Paymob.RefundCallNotConfirmed", utcNow);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure(new Error("CareHomes.Refund.OutcomeAmbiguous",
                "The payment provider could not confirm the refund outcome; an administrator must reconcile the attempt before any further refund is made."));
        }

        if (result.IsFailure)
        {
            switch (ClassifyFailure(result.Error))
            {
                case RefundFailureClass.DefinitiveNoMovement:
                    // Provider-proven refusal: nothing moved, so the claim is
                    // released and the bounded retry route stays available.
                    booking.MarkRefundFailed(result.Error.Code, utcNow);
                    CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.RefundFailed, booking.Id, utcNow);
                    await db.SaveChangesAsync(cancellationToken);
                    break;
                default:
                    // Decision #17: the gateway did not confirm the outcome, so
                    // the money may already have moved. The pre-call claim (and
                    // its attempt identity) is folded into a durable ambiguity
                    // marker instead of a retriable failure — only an
                    // operator's evidence-based reconciliation may clear it.
                    booking.MarkRefundOutcomeAmbiguous(result.Error.Code, utcNow);
                    await db.SaveChangesAsync(cancellationToken);
                    return Result.Failure(new Error("CareHomes.Refund.OutcomeAmbiguous",
                        "The payment provider could not confirm the refund outcome; an administrator must reconcile the attempt before any further refund is made."));
            }

            return Result.Failure(result.Error);
        }

        booking.MarkRefundInitiated(result.Value, utcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class CancelFamilyCareHomeBookingHandler(ICareHomesDbContext db, IPaymobClient paymob)
    : ICommandHandler<CancelFamilyCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(CancelFamilyCareHomeBookingCommand request, CancellationToken ct)
    {
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(
            x => x.Id == request.BookingId && x.FamilyId == request.FamilyId, ct);
        if (booking is null) return NotFound();

        try
        {
            decimal refundAmount = booking.CancelByFamily(request.UtcNow);
            // A paid booking is announced to the family whatever its award: a
            // zero-refund cancellation (clause 2/6: at or after the scheduled
            // start) is still a cancellation of paid money and must reach the
            // family, while a genuinely unpaid checkout-time cancellation
            // (the long-standing quiet path) is not.
            if (booking.PaymentStatus == CareHomeBookingPaymentStatus.Paid)
                CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.FamilyCancelledPaidBooking, booking.Id, request.UtcNow);
            await db.SaveChangesAsync(ct);
            Result futureExtensions = await CareHomeFutureExtensionRefunds.ProcessAsync(
                db, paymob, booking, CareHomeFutureExtensionRefunds.CairoDate(request.UtcNow), request.UtcNow,
                providerEnded: false, ct);
            if (refundAmount > 0m)
            {
                Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, request.UtcNow, ct);
                if (initiated.IsFailure) return Result<CareHomeBookingResponse>.Failure(initiated.Error);
            }
            if (futureExtensions.IsFailure) return Result<CareHomeBookingResponse>.Failure(futureExtensions.Error);
            return CheckoutHandler.Map(booking);
        }
        catch (DomainException)
        {
            // The only DomainException the aggregate raises on a family
            // cancellation is stored-snapshot integrity (an unreadable
            // persisted terms snapshot or an unknown persisted policy
            // revision): fail closed with a stable code instead of a 500.
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "The booking's stored refund-policy snapshot could not be read; contact an administrator."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while cancellation was being processed."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
}

public sealed class CancelOwnerCareHomeBookingHandler(ICareHomesDbContext db, IPaymobClient paymob)
    : ICommandHandler<CancelOwnerCareHomeBookingCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(CancelOwnerCareHomeBookingCommand request, CancellationToken ct)
    {
        var facility = await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == request.Actor, ct);
        CareHomeBooking? booking = facility is null ? null : await db.Bookings.SingleOrDefaultAsync(
            x => x.Id == request.BookingId && x.FacilityId == facility.Id, ct);
        if (booking is null) return NotFound();

        try
        {
            booking.CancelByFacility(request.Reason, request.UtcNow);
            CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.OwnerCancelledAcceptedBooking, booking.Id, request.UtcNow);
            await db.SaveChangesAsync(ct);
            // The facility ended its own stay: its unused-but-started extensions
            // refund in full under any revision (a provider failure is not an
            // act the customer's timing can shrink).
            Result futureExtensions = await CareHomeFutureExtensionRefunds.ProcessAsync(
                db, paymob, booking, CareHomeFutureExtensionRefunds.CairoDate(request.UtcNow), request.UtcNow,
                providerEnded: true, ct);
            Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, request.UtcNow, ct);
            if (initiated.IsFailure) return Result<CareHomeBookingResponse>.Failure(initiated.Error);
            if (futureExtensions.IsFailure) return Result<CareHomeBookingResponse>.Failure(futureExtensions.Error);
            return CheckoutHandler.Map(booking);
        }
        catch (DomainException)
        {
            // As on the family path: stored-snapshot integrity fails closed
            // with a stable code. (The decision award itself is a full refund
            // here, so the aggregate cannot raise this on the provider path's
            // own money logic — only the response mapping re-reading a stored
            // v2 snapshot can.)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "The booking's stored refund-policy snapshot could not be read; contact an administrator."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while cancellation was being processed."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
}

public sealed class RetryCareHomeRefundHandler(ICareHomesDbContext db, IPaymobClient paymob)
    : ICommandHandler<RetryCareHomeRefundCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(RetryCareHomeRefundCommand request, CancellationToken ct)
    {
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (booking is null) return NotFound();
        // A definitive failed refund is retryable; so is a pending refund with
        // no claim — the crash window between the begin-refund save and the
        // claim save, where a claim-always-precedes-a-send proves nothing was
        // sent. A claimed (live, crashed, or marker-folded) attempt is never
        // retryable from here; the send claims atomically.
        if (!booking.IsRefundSendArmed)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.RefundNotRetryable",
                "Only a failed refund, or a pending refund that was never claimed or sent, can be retried."));

        try
        {
            Result initiated = await CareHomeRefundProcessor.InitiateAsync(db, paymob, booking, request.UtcNow, ct);
            if (initiated.IsFailure) return Result<CareHomeBookingResponse>.Failure(initiated.Error);
            return CheckoutHandler.Map(booking);
        }
        catch (DomainException)
        {
            // Stored-snapshot integrity on the response read-back: fail closed
            // with a stable code. The retry gate itself is untouched.
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "The booking's stored refund-policy snapshot could not be read; contact an administrator."));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.RefundNotRetryable", "The refund changed while retry was being claimed."));
        }
    }

    private static Result<CareHomeBookingResponse> NotFound() =>
        Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
}

public sealed class RecordCareHomeRefundCompletedHandler(ICareHomesDbContext db)
    : ICommandHandler<RecordCareHomeRefundCompletedCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(RecordCareHomeRefundCompletedCommand request, CancellationToken ct)
    {
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (booking is null)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));
        try
        {
            booking.MarkRefundManuallyCompleted(request.Reference, request.Reason, request.Actor, request.UtcNow);
            CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.RefundCompleted, booking.Id, request.UtcNow);
            await db.SaveChangesAsync(ct);
            return CheckoutHandler.Map(booking);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DomainException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "The booking's stored refund-policy snapshot could not be read; contact an administrator."));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", "The booking changed while refund completion was being recorded."));
        }
    }
}

public sealed class CompleteCareHomeRefundCallbackHandler(ICareHomesDbContext db)
    : ICommandHandler<CompleteCareHomeRefundCallbackCommand>
{
    public async Task<Result> Handle(CompleteCareHomeRefundCallbackCommand request, CancellationToken ct)
    {
        if (!request.IsRefunded) return Result.Success();
        string refundReference = request.TransactionId.ToString();
        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(
            x => x.RefundReference == refundReference
                || (request.ParentTransactionId.HasValue && x.PaymobTransactionId == request.ParentTransactionId)
                || x.PaymobTransactionId == request.TransactionId, ct);
        if (booking is null) return Result.Success();
        if (booking.MarkRefundCompletedFromProviderCallback(refundReference, request.ParentTransactionId, request.UtcNow))
        {
            CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.RefundCompleted, booking.Id, request.UtcNow);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Result.Success(); }
        }
        return Result.Success();
    }
}

/// <summary>
/// Operator reconciliation of an unconfirmed refund attempt (decision #17).
/// This command is the only way an ambiguity marker is cleared, and it always
/// records who cleared it and on what evidence: <c>refundDidMove</c> states
/// what the operator <i>verified</i> — a verified movement completes the refund
/// for exactly the recorded award, once; a verified non-payment returns the
/// booking to the ordinary failed-refund state the existing retry route
/// handles, which is the only path back to a provider attempt. Evidence must
/// accompany the call, bounded by the composed clearance column length before
/// anything is mutated.
/// <para>
/// The request must carry <c>expectedAttemptClaimId</c>, the attempt identity
/// the operator read from the booking before verifying their evidence: it is
/// compared against the booking's current in-flight claim before any
/// reconciliation happens, so a stale operator (or one reading a superseded
/// attempt) is refused with a stable conflict instead of reconciling a
/// different attempt than the one they verified.
/// </para>
/// <para>
/// Reconciliation of a claimed-but-unmarked attempt (a crash window with no
/// recorded provider outcome) is deliberately refused with
/// <c>CareHomes.RefundReconciliation.AttemptInconclusive</c>: nothing in this
/// module can prove the provider call is no longer in flight (elapsed age is
/// not evidence), and resetting such a claim could race a live send into a
/// second refund. A provider-side attempt-liveness/idempotency seam (an
/// idempotency key stored with the attempt, or a provider status query) is the
/// queued shared dependency that would make that window recoverable; it is
/// requested in the lane report, not implemented here.
/// </para>
/// </summary>
public sealed record ReconcileCareHomeRefundCommand(
    UserId Actor, Guid BookingId, Guid ExpectedAttemptClaimId, bool RefundDidMove,
    string? VerifiedProviderReference, string Evidence, DateTime UtcNow) : ICommand<CareHomeBookingResponse>;

/// <summary>
/// Bounded clearance-evidence composition for reconciliations. The evidence is
/// required, emitted as the composed form
/// <c>"{actor-guid:N}: {evidence}"</c> the aggregate stores in
/// <see cref="CareHomeBooking.RefundAmbiguityClearedBy"/>, and checked against
/// that column's actual maxlength <b>before</b> any caller may load, mutate, or
/// save the booking — so an oversized submission is a stable validation failure
/// instead of a database error surfaced late.
/// </summary>
public static class CareHomeRefundReconciliationEvidence
{
    /// <summary>The maxlength of <see cref="CareHomeBooking.RefundAmbiguityClearedBy"/>.</summary>
    public const int MaxComposedLength = 2000;

    /// <summary>Characters the actor prefix always contributes ("<c>{guid:N}: </c>").</summary>
    public const int ActorPrefixLength = 34;

    public static Result<string> Compose(UserId actor, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return Result<string>.Failure(new Error("CareHomes.RefundReconciliation.EvidenceRequired",
                "A reconciliation must record the evidence the operator used."));
        string composed = $"{actor.Value:N}: {evidence.Trim()}";
        if (composed.Length > MaxComposedLength)
            return Result<string>.Failure(new Error("CareHomes.RefundReconciliation.EvidenceTooLong",
                $"The reconciliation evidence must not exceed {MaxComposedLength - ActorPrefixLength} characters."));
        return Result<string>.Success(composed);
    }
}

public sealed class ReconcileCareHomeRefundHandler(ICareHomesDbContext db)
    : ICommandHandler<ReconcileCareHomeRefundCommand, CareHomeBookingResponse>
{
    public async Task<Result<CareHomeBookingResponse>> Handle(ReconcileCareHomeRefundCommand request, CancellationToken ct)
    {
        // The attempt id is the compare-and-swap key and has no safe default:
        // an absent (empty) id is a stable validation failure — reported
        // before any load or mutation — while authentication itself is
        // enforced by the endpoint exactly as before.
        if (request.ExpectedAttemptClaimId == Guid.Empty)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.RefundReconciliation.InvalidAttemptClaim",
                "The reconciliation must carry the booking's current refund attempt id (refundAttemptClaimId from the admin booking detail)."));
        // Evidence is validated — required here, and bounded by the length of
        // the column it will actually be composed into — before any load,
        // mutation, or save: an oversized submission can never half-apply.
        Result<string> reconciled = CareHomeRefundReconciliationEvidence.Compose(request.Actor, request.Evidence);
        if (reconciled.IsFailure)
            return Result<CareHomeBookingResponse>.Failure(reconciled.Error);
        string reconciledBy = reconciled.Value;

        CareHomeBooking? booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (booking is null)
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.NotFound", "Booking was not found."));

        try
        {
            // Compare-and-swap on the attempt identity: the operator must hold
            // exactly the attempt they verified. A mismatch means the booking
            // moved on — the operator's target was reconciled already, or a
            // newer attempt replaced it — and is refused with a stable
            // conflict so the operator re-reads instead of acting blind.
            if (booking.RefundAttemptClaimId != request.ExpectedAttemptClaimId)
                return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.RefundReconciliation.AttemptMismatch",
                    "The refund attempt changed since the operator read the booking; re-read the booking and use its current attempt id."));

            // Only a raised ambiguity marker — proof that the provider call
            // itself returned, uncertainly — is reconcilable. A claimed-but-
            // unmarked attempt stays untouched: see the command's contract.
            if (booking.IsRefundOutcomeAmbiguous)
                booking.ReconcileAmbiguousRefund(reconciledBy, request.RefundDidMove, request.VerifiedProviderReference, request.ExpectedAttemptClaimId, request.UtcNow);
            else if (booking.Status == CareHomeBookingStatus.RefundPending && booking.RefundClaimedOnUtc is not null)
                return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.RefundReconciliation.AttemptInconclusive",
                    "The recorded refund attempt has no confirmed provider outcome, so it cannot be proven finished; the claim is never reset on evidence or age. Reconciliation of interrupted claims awaits the provider attempt-liveness seam."));
            else
                return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.RefundReconciliation.NoWindowToReconcile",
                    "This booking has no unconfirmed refund attempt to reconcile."));

            if (booking.Status == CareHomeBookingStatus.Refunded)
                CareHomeNotificationEvents.Enqueue(db, CareHomeNotificationEvent.RefundCompleted, booking.Id, request.UtcNow);
            // The response is mapped from the in-memory aggregate BEFORE the
            // committing save: mapping is a pure read of exactly the state
            // that is about to commit, so a post-reconcile mapping failure
            // (e.g. an unreadable stored snapshot on the response view) is a
            // stable error with nothing persisted — never an error reported
            // after the reconciliation itself committed. A committed outcome
            // is final: the returned body is exactly the committed state, and
            // a client that doubts it re-reads the same booking.
            CareHomeBookingResponse response = CheckoutHandler.Map(booking);
            await db.SaveChangesAsync(ct);
            return response;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.InvalidState", ex.Message));
        }
        catch (DomainException)
        {
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.Bookings.UnreadableSnapshot",
                "The booking's stored refund-policy snapshot could not be read; contact an administrator."));
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent writer (e.g. a provider-callback completion) landed
            // between the operator's read and save: the fused check on the
            // version token refuses the stale write with a stable conflict.
            return Result<CareHomeBookingResponse>.Failure(new Error("CareHomes.RefundReconciliation.ConcurrencyConflict",
                "The booking changed while the reconciliation was being recorded; re-read the booking and retry with its current attempt id."));
        }
    }
}
