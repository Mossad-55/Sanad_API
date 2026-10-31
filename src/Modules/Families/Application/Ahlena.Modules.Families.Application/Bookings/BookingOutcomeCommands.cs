using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Domain.Bookings;

namespace Ahlena.Modules.Families.Application.Bookings;

/// <summary>
/// Raised by the assigned caregiver to record that they cancelled an accepted booking (Owner
/// decision #16: a provider cancellation is full refund evidence for the affected sessions), or to
/// record a no-show they did not contest. The care booking is decided by the booking's sold policy
/// revision and the award is refunded in full.
/// </summary>
public sealed record CaregiverRecordProviderOutcomeCommand(
    CaregiverId CaregiverId,
    UserId ActorUserId,
    BookingId BookingId,
    int OutcomeReason,
    string Reason,
    int? ReasonCategory,
    DateTime UtcNow) : ICommand;

public sealed class CaregiverRecordProviderOutcomeCommandHandler
    : ICommandHandler<CaregiverRecordProviderOutcomeCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingCancellationFactRecorder _cancellationFactRecorder;

    public CaregiverRecordProviderOutcomeCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingCancellationFactRecorder cancellationFactRecorder)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _cancellationFactRecorder = cancellationFactRecorder;
    }

    public async Task<Result> Handle(
        CaregiverRecordProviderOutcomeCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // An outcome the system cannot name must fail the request rather than pick a
            // refund path, so an unknown reason is rejected before any state is read.
            if (!Enum.IsDefined(typeof(BookingRefundOutcomeReason), request.OutcomeReason))
            {
                return Result.Failure(new Error(
                    "Bookings.Outcome.InvalidReason",
                    "The supplied provider outcome reason is not a defined reason."));
            }

            BookingRefundOutcomeReason outcomeReason = (BookingRefundOutcomeReason)request.OutcomeReason;

            // Only the reasons a caregiver may attest are accepted here. Lateness beyond the
            // threshold and recipient inaccessibility are recorded through their own paths, and
            // force majeure is not a caregiver statement.
            if (outcomeReason is not (BookingRefundOutcomeReason.ProviderCancelled
                or BookingRefundOutcomeReason.ProviderNoShow))
            {
                return Result.Failure(new Error(
                    "Bookings.Outcome.ReasonNotAllowed",
                    "A caregiver may only record a provider cancellation or a no-show."));
            }

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(
                    b => b.Id == request.BookingId && b.CaregiverId == request.CaregiverId,
                    cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found for this caregiver."));
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                return Result.Failure(new Error(
                    "Bookings.Domain.InvalidOperation",
                    "A provider outcome is only recorded for an accepted booking."));
            }

            if (!BookingOutcomeFeedback.TryCreateAccepted(
                request.ReasonCategory,
                request.Reason,
                out BookingCancellationFeedback? feedback,
                out Error? feedbackError))
            {
                return Result.Failure(feedbackError!);
            }

            BookingRefundPolicyInput input = BookingRefundPolicyInput.ForOutcome(
                booking,
                request.UtcNow,
                feedback,
                allocations: booking.SessionAllocations.Count > 0
                    ? booking.SessionAllocations
                    : null);

            BookingRefundDecision decision = BookingRefundPolicy.DecideOutcome(input, outcomeReason);

            // State transition first, on the untouched-aggregate decision above.
            booking.CancelByCaregiver(request.Reason, request.UtcNow);

            // The disposition fact is recorded BEFORE the external call, so the award is durable
            // even if the process dies between the claim and the provider response.
            BookingCancellationFact fact = BookingCancellationFact.Create(
                booking.Id,
                request.ActorUserId,
                decision,
                feedback);

            await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

            // Claim + commit BEFORE the provider send; the committed claim makes the attempt
            // exclusive, so a competing request cannot also reach the provider.
            Guid? claimId = null;

            if (decision.AwardedMinorUnits > 0
                && BookingRefundSettlementExecutor.HasProviderCapture(booking))
            {
                claimId = Guid.NewGuid();
                booking.ClaimRefundAttempt(claimId.Value, decision.AwardedMinorUnits, request.UtcNow);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(new Error(
                    "Bookings.Cancel.AlreadyProcessed",
                    "This booking's refund was claimed by another request."));
            }

            // The award — not the whole captured amount — is what moves, and the executor is the
            // one place that both moves it and records the settled amount.
            BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
                booking,
                decision.AwardedMinorUnits,
                _paymobClient,
                request.UtcNow,
                cancellationToken,
                claimId);

            // The settlement is never discarded because a competing writer won the row: a late
            // provider confirmation is saved idempotently against the durable award.
            if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                    _dbContext,
                    booking.Id,
                    settlement.SettledMinorUnits,
                    cancellationToken))
            {
                return Result.Failure(BookingRefundSettlementErrors.SettlementIncomplete);
            }

            if (settlement.Destination != BookingRefundDestination.None && !settlement.IsCompleted)
            {
                // The disposition is recorded and durable; only the provider movement failed, so
                // the retryable settlement failure is reported instead of a transition error.
                return Result.Failure(
                    settlement.Error ?? BookingRefundSettlementErrors.SettlementIncomplete);
            }

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error(
                "Bookings.Cancel.AlreadyProcessed",
                "This booking outcome or its refund was already processed."));
        }
        catch (DbUpdateException exception) when (CancellationPersistenceGuard.IsFactUniqueViolation(exception))
        {
            return Result.Failure(new Error(
                "Bookings.Cancel.AlreadyProcessed",
                "This booking cancellation was already processed."));
        }
    }
}

/// <summary>
/// Records a provider-outcome disposition on behalf of the platform (Ahlena / an administrator):
/// a lateness finding beyond the 60-minute threshold, an inability to access the recipient, a
/// safety cancellation, or a force-majeure event (clause 4 and its Owner amendment). The award is
/// decided by the booking's sold policy revision and settles to the payment provider through the
/// same executor the caregiver path uses, so both paths move money identically.
/// </summary>
public sealed record AdminRecordBookingOutcomeCommand(
    UserId ActorUserId,
    BookingId BookingId,
    int OutcomeReason,
    string Reason,
    int? ReasonCategory,
    int? ProviderLatenessMinutes,
    bool CustomerConsentedToLateness,
    bool ForceMajeure,
    DateTime UtcNow) : ICommand;

public sealed class AdminRecordBookingOutcomeCommandHandler
    : ICommandHandler<AdminRecordBookingOutcomeCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPaymobClient _paymobClient;
    private readonly IBookingCancellationFactRecorder _cancellationFactRecorder;

    public AdminRecordBookingOutcomeCommandHandler(
        IFamiliesDbContext dbContext,
        IPaymobClient paymobClient,
        IBookingCancellationFactRecorder cancellationFactRecorder)
    {
        _dbContext = dbContext;
        _paymobClient = paymobClient;
        _cancellationFactRecorder = cancellationFactRecorder;
    }

    public async Task<Result> Handle(
        AdminRecordBookingOutcomeCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.IsDefined(typeof(BookingRefundOutcomeReason), request.OutcomeReason))
            {
                return Result.Failure(new Error(
                    "Bookings.Outcome.InvalidReason",
                    "The supplied provider outcome reason is not a defined reason."));
            }

            BookingRefundOutcomeReason outcomeReason = (BookingRefundOutcomeReason)request.OutcomeReason;

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found."));
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                return Result.Failure(new Error(
                    "Bookings.Domain.InvalidOperation",
                    "A provider outcome is only recorded for an accepted booking."));
            }

            if (!BookingOutcomeFeedback.TryCreateAccepted(
                request.ReasonCategory,
                request.Reason,
                out BookingCancellationFeedback? feedback,
                out Error? feedbackError))
            {
                return Result.Failure(feedbackError!);
            }

            // The recipient-inaccessible disposition is decided by the ordinary family path, so the
            // input is built with the family actor: only that path reaches the access rule and
            // reports the denial. Every other outcome is a recorded provider/safety/force-majeure
            // reason asserted through its own entry point.
            bool inaccessible = outcomeReason == BookingRefundOutcomeReason.RecipientInaccessible;

            BookingRefundPolicyInput input = inaccessible
                ? BookingRefundPolicyInput.ForFamilyCancellation(
                    booking,
                    request.UtcNow,
                    feedback,
                    allocations: booking.SessionAllocations.Count > 0
                        ? booking.SessionAllocations
                        : null,
                    recipientWasInaccessible: true)
                : BookingRefundPolicyInput.ForOutcome(
                    booking,
                    request.UtcNow,
                    feedback,
                    request.ProviderLatenessMinutes,
                    request.CustomerConsentedToLateness,
                    recipientWasInaccessible: false,
                    allocations: booking.SessionAllocations.Count > 0
                        ? booking.SessionAllocations
                        : null,
                    forceMajeure: request.ForceMajeure);

            BookingRefundDecision decision = inaccessible
                ? BookingRefundPolicy.Decide(input)
                : BookingRefundPolicy.DecideOutcome(input, outcomeReason);

            booking.CancelByCaregiver(request.Reason ?? string.Empty, request.UtcNow);

            // The disposition fact is recorded BEFORE the external call, so the award is durable
            // even if the process dies between the claim and the provider response.
            BookingCancellationFact fact = BookingCancellationFact.Create(
                booking.Id,
                request.ActorUserId,
                decision,
                feedback);

            await _cancellationFactRecorder.RecordAsync(fact, cancellationToken);

            // Claim + commit BEFORE the provider send; the committed claim makes the attempt
            // exclusive, so a competing request cannot also reach the provider.
            Guid? claimId = null;

            if (decision.AwardedMinorUnits > 0
                && BookingRefundSettlementExecutor.HasProviderCapture(booking))
            {
                claimId = Guid.NewGuid();
                booking.ClaimRefundAttempt(claimId.Value, decision.AwardedMinorUnits, request.UtcNow);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure(new Error(
                    "Bookings.Cancel.AlreadyProcessed",
                    "This booking's refund was claimed by another request."));
            }

            // One settlement path for both surfaces: the executor sends the award, records the
            // settled amount, and marks an unconfirmed outcome instead of swallowing it.
            BookingRefundSettlement settlement = await BookingRefundSettlementExecutor.SettleWithProviderAsync(
                booking,
                decision.AwardedMinorUnits,
                _paymobClient,
                request.UtcNow,
                cancellationToken,
                claimId);

            // The settlement is never discarded because a competing writer won the row: a late
            // provider confirmation is saved idempotently against the durable award.
            if (!await BookingRefundPersistence.TrySaveClaimedOutcomeAsync(
                    _dbContext,
                    booking.Id,
                    settlement.SettledMinorUnits,
                    cancellationToken))
            {
                return Result.Failure(BookingRefundSettlementErrors.SettlementIncomplete);
            }

            if (settlement.Destination != BookingRefundDestination.None && !settlement.IsCompleted)
            {
                // The disposition and its award are durable; only the movement failed, so report
                // the retryable failure rather than pretending the outcome was recorded whole.
                return Result.Failure(
                    settlement.Error ?? BookingRefundSettlementErrors.SettlementIncomplete);
            }

            return Result.Success();
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error(
                "Bookings.Cancel.AlreadyProcessed",
                "This booking outcome or its refund was already processed."));
        }
        catch (DbUpdateException exception) when (CancellationPersistenceGuard.IsFactUniqueViolation(exception))
        {
            return Result.Failure(new Error(
                "Bookings.Cancel.AlreadyProcessed",
                "This booking cancellation was already processed."));
        }
    }
}

/// <summary>
/// Operator reconciliation of an unconfirmed refund attempt (Owner decision #17). The command is
/// the only thing that clears the booking's ambiguity marker, and it records who cleared it and
/// what was found; until it runs, every refund path on the booking is refused. The Finance
/// complaint/review case correlation is deliberately not wired here: it needs the W4 case seam,
/// so this wave clears the marker with the operator's own evidence only.
/// </summary>
public sealed record ReconcileBookingRefundAmbiguityCommand(
    UserId ActorUserId,
    BookingId BookingId,
    string Evidence,
    bool RefundDidMove,
    DateTime UtcNow,
    Guid? ExpectedAttemptClaimId = null,
    string? VerifiedProviderReference = null) : ICommand;

public sealed class ReconcileBookingRefundAmbiguityCommandHandler
    : ICommandHandler<ReconcileBookingRefundAmbiguityCommand>
{
    private readonly IFamiliesDbContext _dbContext;

    public ReconcileBookingRefundAmbiguityCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        ReconcileBookingRefundAmbiguityCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Evidence))
            {
                return Result.Failure(new Error(
                    "Bookings.RefundReconciliation.EvidenceRequired",
                    "A reconciliation must record the evidence the operator used."));
            }

            Booking? booking = await _dbContext.Bookings
                .SingleOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);

            if (booking is null)
            {
                return Result.Failure(new Error("Bookings.NotFound", "Booking not found."));
            }

            if (!booking.IsRefundOutcomeAmbiguous && !booking.IsRefundAttemptInFlight)
            {
                return Result.Failure(new Error(
                    "Bookings.RefundReconciliation.NoAmbiguousAttempt",
                    "This booking has no unconfirmed or interrupted refund attempt to reconcile."));
            }

            // Bind the decision to the attempt the operator actually inspected. A stale action
            // prepared for attempt N must never clear or book attempt N+1.
            if (booking.RefundAttemptClaimId is { } recordedClaimId
                && request.ExpectedAttemptClaimId != recordedClaimId)
            {
                return Result.Failure(new Error(
                    "Bookings.RefundReconciliation.AttemptMismatch",
                    "The booking's current refund attempt is not the attempt this reconciliation was prepared for; refresh the booking and retry."));
            }

            // Money may only be booked as moved against the provider's own reference: an operator
            // note alone is not evidence that a refund left the gateway.
            if (request.RefundDidMove && string.IsNullOrWhiteSpace(request.VerifiedProviderReference))
            {
                return Result.Failure(new Error(
                    "Bookings.RefundReconciliation.ProviderReferenceRequired",
                    "A reconciliation that records money as refunded must include the provider's refund reference."));
            }

            // A claim that is still in flight may still be moving money at the provider. It can
            // never be cleared as "did not move" on an operator's assertion: only a verified
            // provider reference may conclude it (decision #17).
            if (!request.RefundDidMove && booking.IsRefundAttemptInFlight)
            {
                return Result.Failure(new Error(
                    "Bookings.RefundReconciliation.ClaimInFlight",
                    "The refund attempt is still in flight; without a verified provider reference it cannot be cleared, because the provider call may still move the money."));
            }

            // The audit record is bounded before anything is mutated, so an oversized note cannot
            // fail after the ledger movement was staged.
            string reconciledBy = string.IsNullOrWhiteSpace(request.VerifiedProviderReference)
                ? $"{request.ActorUserId.Value:N}: {request.Evidence.Trim()}"
                : $"{request.ActorUserId.Value:N} ref:{request.VerifiedProviderReference.Trim()}: {request.Evidence.Trim()}";

            if (reconciledBy.Length > 300)
            {
                return Result.Failure(new Error(
                    "Bookings.RefundReconciliation.EvidenceTooLong",
                    "The reconciliation record must fit 300 characters; shorten the evidence or provider reference."));
            }

            // What the operator found decides the ledger, never a guess: a refund that did move is
            // recorded against the ceiling (which also re-arms the decision #4 guard), and one that
            // did not leaves the ceiling untouched so the award can be re-attempted.
            //
            // The award comes from whichever record the attempt left: the ambiguity marker, or the
            // in-flight claim of an attempt that crashed before its outcome was saved. Both are
            // persisted before any external call, so one of them is always the authoritative figure.
            if (request.RefundDidMove)
            {
                long? attemptedAward = booking.RefundAmbiguityAwardMinorUnits
                    ?? booking.RefundAttemptAwardMinorUnits;

                if (attemptedAward is not { } award || award <= 0)
                {
                    return Result.Failure(new Error(
                        "Bookings.RefundReconciliation.AwardUnknown",
                        "The unconfirmed attempt did not record an award, so a completed movement cannot be booked."));
                }

                booking.RecordRefundedAmount(award, request.UtcNow);
            }

            // An ambiguous attempt is cleared through the marker; a crashed pre-send claim had no
            // provider result, so it is cleared through the claim it left in flight. Either way
            // this is the only path that reopens refund attempts on the booking.
            if (booking.IsRefundOutcomeAmbiguous)
            {
                booking.ClearRefundAmbiguity(reconciledBy, request.UtcNow);
            }
            else
            {
                booking.ClearInterruptedRefundAttempt(reconciledBy, request.UtcNow);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(new Error(
                "Bookings.RefundReconciliation.ConcurrencyConflict",
                "The booking changed while the reconciliation was being recorded; re-read it and retry with its current attempt id."));
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error("Bookings.Domain.InvalidOperation", exception.Message));
        }
    }
}

/// <summary>
/// Shared feedback-shape validation for the outcome commands. Every outcome these commands record
/// applies to a booking the caregiver had already accepted, so the recorded reason must be the
/// accepted-booking shape — a known category plus a non-blank note — exactly as the ordinary
/// cancellation path requires. Reusing one helper keeps the two paths from drifting, so a provider
/// outcome can never be recorded with a reason the fact boundary would refuse.
/// </summary>
internal static class BookingOutcomeFeedback
{
    public static bool TryCreateAccepted(
        int? reasonCategory,
        string? reason,
        out BookingCancellationFeedback? feedback,
        out Error? error)
    {
        feedback = null;
        error = null;

        if (reasonCategory is null)
        {
            error = new Error(
                "Bookings.Cancel.ReasonCategoryRequired",
                "A reason category is required to record an outcome for an accepted booking.");
            return false;
        }

        if (!BookingCancellationReasonCategories.TryParse(
            reasonCategory.Value,
            out BookingCancellationReasonCategory category))
        {
            error = new Error(
                "Bookings.Cancel.ReasonCategoryInvalid",
                "The supplied reason category is not a defined cancellation reason.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            error = new Error(
                "Bookings.Cancel.ReasonRequired",
                "A reason note is required to record an outcome for an accepted booking.");
            return false;
        }

        feedback = BookingCancellationFeedback.Create(category, reason);
        return true;
    }
}
