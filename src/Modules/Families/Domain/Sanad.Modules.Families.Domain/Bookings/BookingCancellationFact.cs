using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Durable record of one cancellation decision, kept separate from the mutable
/// <see cref="Booking.Status"/> so a later status change (including <see cref="BookingStatus.Refunded"/>)
/// can never erase who cancelled, why, or that a caregiver incident was flagged.
/// <para>
/// Append-only by design: it exposes no mutation member, so the recorded decision and its history stay
/// immutable. Actor identity is supplied by the authorized application handler that records the fact,
/// never trusted from client claims, and family-role authorization stays an application responsibility.
/// </para>
/// </summary>
public sealed class BookingCancellationFact : Entity<BookingCancellationFactId>
{
    private BookingCancellationFact()
    {
    }

    private BookingCancellationFact(
        BookingCancellationFactId id,
        BookingId bookingId,
        BookingCancellationActorSide actorSide,
        UserId actorUserId,
        BookingCancellationAction action,
        BookingStatus statusAtCancellation,
        DateTime cancelledOnUtc,
        DateTime? confirmedOnUtcUsed,
        int policyVersion,
        BookingCancellationReasonCategory? reasonCategory,
        string? reasonNote,
        BookingRefundEntitlement refundEntitlement,
        BookingRefundDecisionReason refundDecisionReason,
        bool isCaregiverIncident)
        : base(id)
    {
        BookingId = bookingId;
        ActorSide = actorSide;
        ActorUserId = actorUserId;
        Action = action;
        StatusAtCancellation = statusAtCancellation;
        CancelledOnUtc = cancelledOnUtc;
        ConfirmedOnUtcUsed = confirmedOnUtcUsed;
        PolicyVersion = policyVersion;
        ReasonCategory = reasonCategory;
        ReasonNote = reasonNote;
        RefundEntitlement = refundEntitlement;
        RefundDecisionReason = refundDecisionReason;
        IsCaregiverIncident = isCaregiverIncident;
    }

    public BookingId BookingId { get; private set; }

    public BookingCancellationActorSide ActorSide { get; private set; }

    public UserId ActorUserId { get; private set; }

    public BookingCancellationAction Action { get; private set; }

    public BookingStatus StatusAtCancellation { get; private set; }

    public DateTime CancelledOnUtc { get; private set; }

    public DateTime? ConfirmedOnUtcUsed { get; private set; }

    public int PolicyVersion { get; private set; }

    public BookingCancellationReasonCategory? ReasonCategory { get; private set; }

    public string? ReasonNote { get; private set; }

    public BookingRefundEntitlement RefundEntitlement { get; private set; }

    public BookingRefundDecisionReason RefundDecisionReason { get; private set; }

    public bool IsCaregiverIncident { get; private set; }

    public static BookingCancellationFact Create(
        BookingId bookingId,
        UserId actorUserId,
        BookingCancellationDecision decision,
        BookingCancellationFeedback? feedback)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (bookingId == BookingId.Empty)
            throw new DomainException("Booking ID is required.");

        if (actorUserId == UserId.Empty)
            throw new DomainException("Cancelling user ID is required.");

        if (decision.IsReasonFeedbackRequired && feedback is null)
            throw new DomainException(
                "Cancelling an accepted booking requires a reason category and a note.");

        return new BookingCancellationFact(
            BookingCancellationFactId.New(),
            bookingId,
            decision.ActorSide,
            actorUserId,
            decision.Action,
            decision.StatusAtCancellation,
            decision.CancelledOnUtc,
            decision.ConfirmedOnUtcUsed,
            decision.PolicyVersion,
            feedback?.Category,
            feedback?.Note,
            decision.RefundEntitlement,
            decision.RefundDecisionReason,
            decision.IsCaregiverIncident);
    }
}
