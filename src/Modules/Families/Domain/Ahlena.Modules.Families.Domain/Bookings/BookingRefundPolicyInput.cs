namespace Ahlena.Modules.Families.Domain.Bookings;

/// <summary>
/// Everything <see cref="BookingRefundPolicy"/> is allowed to look at. Every time is an
/// authoritative UTC value supplied by the caller (the server clock provider and persisted booking
/// timestamps); the policy reads no clock, so a decision is reproducible from the same input.
/// <para>
/// The scheduled start is a first-class field rather than something derived from the booking's
/// date and time-of-day, because clause 2 states the timing rule explicitly "relative to scheduled
/// service, not provider acceptance" and the boundary is tick-exact at 24 hours. Passing the
/// resolved instant keeps that comparison in one place and lets the policy refuse a missing or
/// non-UTC value instead of guessing a start from an ambiguous local time.
/// </para>
/// <para>
/// Captured money is expressed in piastres minor units (<see cref="long"/>), matching
/// <c>Money.MinorUnits</c> in the Finance money core without this domain referencing it. Previous
/// refunds are supplied so the policy can enforce decision #4 ("cumulative refunds cannot exceed
/// captured funds less previous refunds") itself rather than trusting a caller-side check.
/// </para>
/// </summary>
/// <param name="Status">Persisted <see cref="Booking.Status"/> at the moment of the act.</param>
/// <param name="ActorSide">Who ends the booking.</param>
/// <param name="Action">Cancel, or a caregiver rejection of a booking awaiting approval.</param>
/// <param name="ScheduledStartUtc">Resolved UTC start of the booked service, when known.</param>
/// <param name="ActedOnUtc">Authoritative UTC time of the act (cancellation, decline or outcome).</param>
/// <param name="ConfirmedOnUtc">Persisted acceptance time; null while the booking is unaccepted.</param>
/// <param name="StartedOnUtc">Persisted visit start; non-null means the service has begun.</param>
/// <param name="CaptureEvidence">What is known about money taken for this booking.</param>
/// <param name="CapturedMinorUnits">
/// Total money captured for the booking in piastres. Informational for the entitlement (which is a
/// percentage or a full/no-refund answer) and authoritative for the decision #4 ceiling check.
/// </param>
/// <param name="PreviouslyRefundedMinorUnits">Money already refunded against this booking, in piastres.</param>
/// <param name="Allocations">Per-session allocations of a multi-session package; empty for a single booking.</param>
/// <param name="ProviderLatenessMinutes">Minutes the provider was late, when the act records lateness.</param>
/// <param name="CustomerConsentedToLateness">True when the customer accepted the provider's lateness.</param>
/// <param name="RecipientWasInaccessible">True when the recipient could not be accessed at the appointed time.</param>
/// <param name="ForceMajeure">
/// True when the act records a force-majeure event (Owner amendment to clause 4). Undelivered
/// services and unused sessions are refunded; delivered services remain payable.
/// </param>
/// <param name="Feedback">
/// Validated reason; mandatory once the booking was accepted, optional before acceptance. It is
/// validated by the policy but not recorded here — <see cref="BookingCancellationFact"/> owns it.
/// </param>
public sealed record BookingRefundPolicyInput(
    BookingStatus Status,
    BookingCancellationActorSide ActorSide,
    BookingCancellationAction Action,
    DateTime? ScheduledStartUtc,
    DateTime ActedOnUtc,
    DateTime? ConfirmedOnUtc,
    DateTime? StartedOnUtc,
    BookingCaptureEvidence CaptureEvidence,
    long CapturedMinorUnits,
    long PreviouslyRefundedMinorUnits,
    IReadOnlyList<BookingSessionAllocation>? Allocations,
    int? ProviderLatenessMinutes,
    bool CustomerConsentedToLateness,
    bool RecipientWasInaccessible,
    bool ForceMajeure = false,
    BookingCancellationFeedback? Feedback = null)
{
    /// <summary>The per-session allocations, normalized so callers never see null.</summary>
    public IReadOnlyList<BookingSessionAllocation> AllocationList => Allocations ?? [];

    /// <summary>
    /// Builds the input for a family cancellation from a persisted booking and the caller's
    /// authoritative time. The scheduled start is order-independent and interpreted in UTC, which
    /// is the only representation the system persists.
    /// </summary>
    public static BookingRefundPolicyInput ForFamilyCancellation(
        Booking booking,
        DateTime actedOnUtc,
        BookingCancellationFeedback? feedback = null,
        IReadOnlyList<BookingSessionAllocation>? allocations = null,
        int? providerLatenessMinutes = null,
        bool customerConsentedToLateness = false,
        bool recipientWasInaccessible = false,
        bool forceMajeure = false)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new BookingRefundPolicyInput(
            booking.Status,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            ResolveScheduledStart(booking),
            actedOnUtc,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking),
            CapturedMinorUnitsOf(booking),
            booking.RefundedMinorUnits,
            allocations,
            providerLatenessMinutes,
            customerConsentedToLateness,
            recipientWasInaccessible,
            ForceMajeure: forceMajeure,
            Feedback: feedback);
    }

    /// <summary>Builds the input for a caregiver rejection of a booking still awaiting approval.</summary>
    public static BookingRefundPolicyInput ForCaregiverRejection(
        Booking booking,
        DateTime actedOnUtc,
        BookingCancellationFeedback? feedback = null,
        IReadOnlyList<BookingSessionAllocation>? allocations = null)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new BookingRefundPolicyInput(
            booking.Status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Reject,
            ResolveScheduledStart(booking),
            actedOnUtc,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking),
            CapturedMinorUnitsOf(booking),
            booking.RefundedMinorUnits,
            allocations,
            ProviderLatenessMinutes: null,
            CustomerConsentedToLateness: false,
            RecipientWasInaccessible: false,
            Feedback: feedback);
    }

    /// <summary>Builds the input for a caregiver cancellation of a booking they had accepted.</summary>
    public static BookingRefundPolicyInput ForCaregiverCancellation(
        Booking booking,
        DateTime actedOnUtc,
        BookingCancellationFeedback? feedback = null,
        IReadOnlyList<BookingSessionAllocation>? allocations = null)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new BookingRefundPolicyInput(
            booking.Status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Cancel,
            ResolveScheduledStart(booking),
            actedOnUtc,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking),
            CapturedMinorUnitsOf(booking),
            booking.RefundedMinorUnits,
            allocations,
            ProviderLatenessMinutes: null,
            CustomerConsentedToLateness: false,
            RecipientWasInaccessible: false,
            Feedback: feedback);
    }

    /// <summary>
    /// Builds the input for a provider-side, access or force-majeure outcome. The outcome reason is
    /// resolved by the policy from these facts, so the caller states what happened rather than
    /// choosing an entitlement.
    /// </summary>
    public static BookingRefundPolicyInput ForOutcome(
        Booking booking,
        DateTime actedOnUtc,
        BookingCancellationFeedback? feedback = null,
        int? providerLatenessMinutes = null,
        bool customerConsentedToLateness = false,
        bool recipientWasInaccessible = false,
        IReadOnlyList<BookingSessionAllocation>? allocations = null,
        bool forceMajeure = false)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new BookingRefundPolicyInput(
            booking.Status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Cancel,
            ResolveScheduledStart(booking),
            actedOnUtc,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            BookingCancellationPolicyInput.ResolveCaptureEvidence(booking),
            CapturedMinorUnitsOf(booking),
            booking.RefundedMinorUnits,
            allocations,
            providerLatenessMinutes,
            customerConsentedToLateness,
            recipientWasInaccessible,
            ForceMajeure: forceMajeure,
            Feedback: feedback);
    }

    /// <summary>
    /// Money captured for a booking in piastres, derived from the persisted facts without a
    /// provider call: a succeeded payment transaction (its own major-unit amount, rounded half to
    /// even like every other money conversion in the codebase), otherwise the booking's immutable
    /// price snapshot total. A booking that was never paid reports zero here even though the
    /// snapshot carries a price — the snapshot states what was <em>owed</em>, not what was taken.
    /// </summary>
    internal static long CapturedMinorUnitsOf(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        PaymentTransaction? captured = booking.PaymentTransactions
            .FirstOrDefault(transaction => transaction.Status == PaymentTransactionStatus.Succeeded);

        if (captured is not null)
            return ToMinorUnits(captured.Amount);

        bool everPaid = booking.PaidOnUtc is not null
            || !string.IsNullOrWhiteSpace(booking.PaymobTransactionId);

        return everPaid ? ToMinorUnits(booking.PriceSnapshot.TotalPayableAmount) : 0L;
    }

    /// <summary>
    /// The resolved UTC start instant of the booked service. This wave persists a booking date
    /// plus start time and no scheduled-start instant, so the start is reconstructed as UTC —
    /// the tenant's canonical representation. Classifying the combined value as UTC is
    /// representation, not guessing a refund boundary: the value is the same date and wall clock
    /// the family booked, and the local bidding-time conversion is deliberately out of scope
    /// because it would need the dependent's time zone (a Caregivers/Identity-boundary concern).
    /// </summary>
    private static DateTime? ResolveScheduledStart(Booking booking) =>
        booking.BookingDate == default || booking.StartTime == default
            ? null
            : DateTime.SpecifyKind(
                booking.BookingDate.ToDateTime(booking.StartTime),
                DateTimeKind.Utc);

    private static long ToMinorUnits(decimal majorUnits) =>
        checked((long)decimal.Round(
            majorUnits * 100m,
            0,
            MidpointRounding.ToEven));
}
