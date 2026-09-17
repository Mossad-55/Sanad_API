using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Deterministic, side-effect-free booking cancellation policy (version 1).
/// <para>
/// Rules, measured from the persisted acceptance time (<c>ConfirmedOnUtc</c>) against the authoritative
/// cancellation time supplied by the caller:
/// <list type="bullet">
/// <item><description>PendingPayment, family cancels: allowed, nothing was captured, no refund due.</description></item>
/// <item><description>PendingCaregiverApproval, family cancels or assigned caregiver rejects: full captured refund, no incident.</description></item>
/// <item><description>Confirmed, assigned caregiver cancels: full captured refund and exactly one caregiver incident flag.</description></item>
/// <item><description>Confirmed, family cancels: full captured refund while elapsed time is under 60 minutes; from exactly 60 minutes onward, no refund. No incident.</description></item>
/// <item><description>InProgress, Completed and every ended state: ordinary cancellation is refused.</description></item>
/// </list>
/// </para>
/// <para>
/// The policy never reads a clock, never calls a payment provider and never produces an amount. It
/// fails explicitly on a missing acceptance time, on a non-UTC or inconsistent timestamp, or on a
/// state/actor combination it does not recognise, instead of guessing a financial entitlement. A
/// booking that already ended is refused, so a recorded cancellation is never re-evaluated by a later
/// policy revision.
/// </para>
/// </summary>
public static class BookingCancellationPolicy
{
    /// <summary>Revision stamped on every decision and cancellation fact.</summary>
    public const int CurrentPolicyVersion = 1;

    /// <summary>
    /// Family grace window after acceptance. Elapsed time strictly below this refunds in full;
    /// elapsed time equal to or above it refunds nothing.
    /// </summary>
    public static readonly TimeSpan FamilyCancellationGraceWindow = TimeSpan.FromMinutes(60);

    public static BookingCancellationDecision Decide(BookingCancellationPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Validate(input);

        BookingRefundEntitlement refundEntitlement;
        BookingRefundDecisionReason refundDecisionReason;

        if (!input.HasCapturedPayment)
        {
            refundEntitlement = BookingRefundEntitlement.NoRefundDue;
            refundDecisionReason = input.Status == BookingStatus.PendingPayment
                ? BookingRefundDecisionReason.NothingCaptured
                : BookingRefundDecisionReason.NoCapturedPaymentRecorded;
        }
        else if (input.Status == BookingStatus.PendingCaregiverApproval)
        {
            refundEntitlement = BookingRefundEntitlement.FullCapturedRefund;
            refundDecisionReason = input.ActorSide == BookingCancellationActorSide.Caregiver
                ? BookingRefundDecisionReason.CaregiverRejectionBeforeAcceptance
                : BookingRefundDecisionReason.FamilyCancellationBeforeAcceptance;
        }
        else if (input.ActorSide == BookingCancellationActorSide.Caregiver)
        {
            refundEntitlement = BookingRefundEntitlement.FullCapturedRefund;
            refundDecisionReason = BookingRefundDecisionReason.CaregiverCancellationAfterAcceptance;
        }
        else if (input.CancelledOnUtc - input.ConfirmedOnUtc!.Value < FamilyCancellationGraceWindow)
        {
            refundEntitlement = BookingRefundEntitlement.FullCapturedRefund;
            refundDecisionReason = BookingRefundDecisionReason.FamilyCancellationWithinGraceWindow;
        }
        else
        {
            refundEntitlement = BookingRefundEntitlement.NoRefundDue;
            refundDecisionReason = BookingRefundDecisionReason.FamilyCancellationOutsideGraceWindow;
        }

        bool isCaregiverIncident =
            input.Status == BookingStatus.Confirmed
            && input.ActorSide == BookingCancellationActorSide.Caregiver
            && input.Action == BookingCancellationAction.Cancel;

        bool isReasonFeedbackRequired = input.Status == BookingStatus.Confirmed;

        if (isReasonFeedbackRequired && input.Feedback is null)
            throw new DomainException(
                "Cancelling an accepted booking requires a reason category and a note.");

        return new BookingCancellationDecision(
            input.Status,
            input.ActorSide,
            input.Action,
            input.CancelledOnUtc,
            input.Status == BookingStatus.Confirmed ? input.ConfirmedOnUtc : null,
            refundEntitlement,
            refundDecisionReason,
            isCaregiverIncident,
            isReasonFeedbackRequired,
            CurrentPolicyVersion);
    }

    private static void Validate(BookingCancellationPolicyInput input)
    {
        if (!Enum.IsDefined(input.Status))
            throw new DomainException("Booking status is invalid.");

        if (!Enum.IsDefined(input.ActorSide))
            throw new DomainException("Cancellation actor side is invalid.");

        if (!Enum.IsDefined(input.Action))
            throw new DomainException("Cancellation action is invalid.");

        ValidateTimestamp(input.CancelledOnUtc, "Cancellation time");

        if (input.ConfirmedOnUtc is not null)
            ValidateTimestamp(input.ConfirmedOnUtc.Value, "Acceptance time");

        if (input.StartedOnUtc is not null)
            ValidateTimestamp(input.StartedOnUtc.Value, "Visit start time");

        switch (input.Status)
        {
            case BookingStatus.InProgress:
            case BookingStatus.Completed:
                throw new DomainException(
                    "A booking whose visit has already started or completed can no longer be cancelled.");

            case BookingStatus.CancelledByFamily:
            case BookingStatus.CancelledByCaregiver:
            case BookingStatus.DeclinedByCaregiver:
            case BookingStatus.Refunded:
            case BookingStatus.Expired:
                throw new DomainException(
                    "This booking has already ended; a recorded cancellation decision is never re-evaluated.");

            case BookingStatus.PendingPayment:
            case BookingStatus.PendingCaregiverApproval:
            case BookingStatus.Confirmed:
                break;
        }

        if (input.ActorSide == BookingCancellationActorSide.Family
            && input.Action == BookingCancellationAction.Reject)
            throw new DomainException("Only a caregiver can reject a booking.");

        if (input.ActorSide == BookingCancellationActorSide.Caregiver)
        {
            if (input.Action == BookingCancellationAction.Reject
                && input.Status != BookingStatus.PendingCaregiverApproval)
                throw new DomainException(
                    "A caregiver can only reject a booking that is still awaiting approval.");

            if (input.Action == BookingCancellationAction.Cancel
                && input.Status != BookingStatus.Confirmed)
                throw new DomainException(
                    "A caregiver can only cancel a booking they have already accepted.");
        }

        if (input.Status == BookingStatus.Confirmed)
        {
            if (input.ConfirmedOnUtc is null)
                throw new DomainException(
                    "An accepted booking must carry a valid acceptance time before it can be cancelled.");

            if (input.StartedOnUtc is not null)
                throw new DomainException(
                    "An accepted booking that already recorded a visit start is not cancellable.");

            if (input.ConfirmedOnUtc.Value > input.CancelledOnUtc)
                throw new DomainException("Cancellation time cannot be earlier than the acceptance time.");
        }
        else
        {
            if (input.ConfirmedOnUtc is not null)
                throw new DomainException(
                    "A booking that was never accepted cannot carry an acceptance time.");

            if (input.StartedOnUtc is not null)
                throw new DomainException(
                    "A booking that was never accepted cannot carry a visit start time.");
        }

        if (input.Status == BookingStatus.PendingPayment && input.HasCapturedPayment)
            throw new DomainException(
                "A booking that is still awaiting payment cannot have a captured payment.");
    }

    private static void ValidateTimestamp(DateTime value, string field)
    {
        if (value == default)
            throw new DomainException($"{field} is required.");

        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"{field} must be a UTC timestamp.");
    }
}
