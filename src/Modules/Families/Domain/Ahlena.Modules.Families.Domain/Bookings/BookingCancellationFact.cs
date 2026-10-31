using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// Durable record of one cancellation decision, kept separate from the mutable
/// <see cref="Booking.Status"/> so a later status change (including <see cref="BookingStatus.Refunded"/>)
/// can never erase who cancelled, why, or that a caregiver incident was flagged.
/// <para>
/// Append-only by design: it exposes no mutation member, so the recorded decision and its history stay
/// immutable. Actor identity is supplied by the authorized application handler that records the fact,
/// never trusted from client claims, and family-role authorization stays an application responsibility.
/// </para>
/// <para>
/// The reason is not a parameter: it is read from the validated decision, so a caller cannot attach an
/// unrelated category or note after the policy has spoken. A decision itself can only come from
/// <see cref="BookingCancellationPolicy.Decide"/>, which is the single validated construction path.
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
        bool isCaregiverIncident,
        long? awardedMinorUnits,
        long? partialEntitlementMinorUnits)
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
        AwardedMinorUnits = awardedMinorUnits;
        PartialEntitlementMinorUnits = partialEntitlementMinorUnits;
    }

    public BookingId BookingId { get; private set; }

    public BookingCancellationActorSide ActorSide { get; private set; }

    public UserId ActorUserId { get; private set; }

    public BookingCancellationAction Action { get; private set; }

    public BookingStatus StatusAtCancellation { get; private set; }

    public DateTime CancelledOnUtc { get; private set; }

    public DateTime? ConfirmedOnUtcUsed { get; private set; }

    public int PolicyVersion { get; private set; }

    /// <summary>
    /// Category recorded with the cancellation, or null when the cancellation happened before
    /// acceptance and only an optional note was supplied. Never fabricated.
    /// </summary>
    public BookingCancellationReasonCategory? ReasonCategory { get; private set; }

    /// <summary>
    /// Trimmed reason note within <see cref="Booking.MaximumReasonLength"/> characters, or null when
    /// nothing was supplied for a pre-acceptance cancellation.
    /// </summary>
    public string? ReasonNote { get; private set; }

    public BookingRefundEntitlement RefundEntitlement { get; private set; }

    public BookingRefundDecisionReason RefundDecisionReason { get; private set; }

    /// <summary>
    /// Recorded independently of <see cref="Booking.Status"/>, so a later refund cannot erase it.
    /// Discretionary admin review only: never automatic suspension, rating change or payout penalty.
    /// </summary>
    public bool IsCaregiverIncident { get; private set; }

    /// <summary>
    /// The amount the revision-2 policy awarded for this cancellation, in piastres. Null for a
    /// revision-1 fact, whose decision is amount-free (a full entitlement there means the whole
    /// captured amount). Carried so an ops retry can reconstruct the award without re-deciding the
    /// cancellation, and so the wire can distinguish a 50% award from a full one.
    /// </summary>
    public long? AwardedMinorUnits { get; private set; }

    /// <summary>
    /// For a partial award, the amount a full refund of the entitlement would have been, in
    /// piastres: the refundable ceiling for a percentage award, or the undelivered sessions'
    /// allocations for a package. Null when the entitlement is not partial, so a caller can never
    /// read a partial award as the whole captured amount.
    /// </summary>
    public long? PartialEntitlementMinorUnits { get; private set; }

    /// <summary>
    /// Records a validated decision for a booking and the acting user. Feedback is taken from the
    /// decision, never supplied separately.
    /// </summary>
    public static BookingCancellationFact Create(
        BookingId bookingId,
        UserId actorUserId,
        BookingCancellationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (bookingId == BookingId.Empty)
            throw new DomainException("Booking ID is required.");

        if (actorUserId == UserId.Empty)
            throw new DomainException("Cancelling user ID is required.");

        // Defensive, derived from the recorded booking state rather than from a trusted flag: a
        // cancellation of an accepted booking can never be recorded without a known category and a
        // bounded note, and a decision that disagrees with the state it was made for is refused.
        bool acceptedBooking = decision.StatusAtCancellation == BookingStatus.Confirmed;

        if (acceptedBooking != decision.IsReasonFeedbackRequired)
            throw new DomainException(
                "Cancellation decision is inconsistent with the booking state it was made for.");

        if (acceptedBooking)
            BookingCancellationFeedback.RequireForAcceptedBooking(decision.Feedback);

        BookingCancellationFeedback? feedback = decision.Feedback;

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
            decision.IsCaregiverIncident,
            awardedMinorUnits: null,
            partialEntitlementMinorUnits: null);
    }

    /// <summary>
    /// Records a revision-2 decision for a booking and the acting user, carrying the awarded
    /// amount and the partial-entitlement basis onto the durable fact.
    /// <para>
    /// This is the bridge the richer decision needs: <see cref="BookingRefundDecision"/> is not a
    /// <see cref="BookingCancellationDecision"/> and deliberately cannot masquerade as one, so the
    /// decision-version-1 shape is produced by
    /// <see cref="BookingRefundDecision.DescribeForCancellationFact"/>. That description keeps the
    /// legacy classification honest for old readers, while the exact money travels separately in
    /// <see cref="AwardedMinorUnits"/> — the amount is never re-derived from the description, so a
    /// partial award can never be recorded as if the whole captured amount had been refunded.
    /// </para>
    /// <para>
    /// Public, because the application layer is where the booking is loaded and the decision is
    /// applied, so it is the only place that can record this fact. What must stay domain-internal
    /// is the <em>construction</em> of the decision, not the recording of it:
    /// <see cref="BookingRefundDecision.Create"/> is internal, so a caller can only ever record a
    /// decision the policy itself produced — it cannot invent an award, an entitlement or a reason.
    /// </para>
    /// </summary>
    public static BookingCancellationFact Create(
        BookingId bookingId,
        UserId actorUserId,
        BookingRefundDecision decision,
        BookingCancellationFeedback? feedback)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (bookingId == BookingId.Empty)
            throw new DomainException("Booking ID is required.");

        if (actorUserId == UserId.Empty)
            throw new DomainException("Cancelling user ID is required.");

        bool acceptedBooking = decision.StatusAtAct == BookingStatus.Confirmed;

        if (acceptedBooking != decision.IsReasonFeedbackRequired)
            throw new DomainException(
                "Cancellation decision is inconsistent with the booking state it was made for.");

        if (acceptedBooking)
            BookingCancellationFeedback.RequireForAcceptedBooking(feedback);

        (BookingRefundEntitlement _,
            BookingRefundDecisionReason reason,
            bool isCaregiverIncident) = decision.DescribeForCancellationFact();

        // The fact stores the decision's OWN entitlement, not the saturated legacy description:
        // DescribeForCancellationFact() saturates any non-zero award to FullCapturedRefund for
        // decision-version-1 readers, and persisting that would re-label a 50% clause-2 award as a
        // full refund — precisely the mislabelling MAJ-6 forbids. The description is still used
        // below for the legacy reason vocabulary and the caregiver-incident flag, where it is
        // accurate.
        BookingRefundEntitlement entitlement = decision.RefundEntitlement;

        // A partial award records the basis it was a slice of — the percentage basis for a family
        // award, or the undelivered sessions' allocations for a package — so the wire can surface
        // the true full-refund figure. A full or denied award records no basis, so nothing
        // downstream can read a full refund as partial by accident.
        long? partialEntitlementMinorUnits = entitlement == BookingRefundEntitlement.PartialRefund
            ? decision.AwardedProrationBasisUnits ?? decision.RefundableCeilingMinorUnits
            : null;

        return new BookingCancellationFact(
            BookingCancellationFactId.New(),
            bookingId,
            decision.ActorSide,
            actorUserId,
            decision.Action,
            decision.StatusAtAct,
            decision.ActedOnUtc,
            decision.ConfirmedOnUtcUsed,
            decision.PolicyVersion.Version,
            feedback?.Category,
            feedback?.Note,
            entitlement,
            reason,
            isCaregiverIncident,
            decision.AwardedMinorUnits,
            partialEntitlementMinorUnits);
    }
}
