namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Everything the cancellation policy is allowed to look at. Times are authoritative values supplied
/// by the caller (server time provider and persisted booking timestamps); the policy never reads a
/// clock itself. Payment facts are supplied as evidence, so the policy never queries a provider.
/// </summary>
/// <param name="Status">Persisted <see cref="Booking.Status"/> at the moment of cancellation.</param>
/// <param name="ActorSide">Who ends the booking.</param>
/// <param name="Action">Cancel, or a caregiver rejection of a booking awaiting approval.</param>
/// <param name="ConfirmedOnUtc">
/// Persisted acceptance time (<c>Booking.ConfirmedOnUtc</c>); null while the booking is unaccepted.
/// </param>
/// <param name="StartedOnUtc">Persisted visit start time (<c>Booking.StartedOnUtc</c>); normally null here.</param>
/// <param name="CancelledOnUtc">Authoritative UTC time of the cancellation act.</param>
/// <param name="CaptureEvidence">What is known about money taken for this booking.</param>
/// <param name="Feedback">
/// Validated reason; mandatory once the booking was accepted, optional before acceptance.
/// </param>
public sealed record BookingCancellationPolicyInput(
    BookingStatus Status,
    BookingCancellationActorSide ActorSide,
    BookingCancellationAction Action,
    DateTime? ConfirmedOnUtc,
    DateTime? StartedOnUtc,
    DateTime CancelledOnUtc,
    BookingCaptureEvidence CaptureEvidence,
    BookingCancellationFeedback? Feedback)
{
    public static BookingCancellationPolicyInput ForFamilyCancellation(
        BookingStatus status,
        DateTime? confirmedOnUtc,
        DateTime? startedOnUtc,
        DateTime cancelledOnUtc,
        BookingCaptureEvidence captureEvidence,
        BookingCancellationFeedback? feedback = null) =>
        new(
            status,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            confirmedOnUtc,
            startedOnUtc,
            cancelledOnUtc,
            captureEvidence,
            feedback);

    public static BookingCancellationPolicyInput ForCaregiverRejection(
        BookingStatus status,
        DateTime cancelledOnUtc,
        BookingCaptureEvidence captureEvidence,
        BookingCancellationFeedback? feedback = null) =>
        new(
            status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Reject,
            null,
            null,
            cancelledOnUtc,
            captureEvidence,
            feedback);

    public static BookingCancellationPolicyInput ForCaregiverCancellation(
        BookingStatus status,
        DateTime? confirmedOnUtc,
        DateTime? startedOnUtc,
        DateTime cancelledOnUtc,
        BookingCaptureEvidence captureEvidence,
        BookingCancellationFeedback? feedback = null) =>
        new(
            status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Cancel,
            confirmedOnUtc,
            startedOnUtc,
            cancelledOnUtc,
            captureEvidence,
            feedback);

    /// <summary>
    /// Builds the input from a persisted booking and the caller's authoritative time.
    /// <para>
    /// Loading assumption: the booking's payment transactions are an owned collection on the aggregate,
    /// so a normal <c>Bookings</c> query already loads them. This helper issues no query, triggers no
    /// lazy loading and calls no provider; a caller projecting a partial booking must resolve capture
    /// evidence itself instead of passing an empty collection.
    /// </para>
    /// </summary>
    public static BookingCancellationPolicyInput FromBooking(
        Booking booking,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        DateTime cancelledOnUtc,
        BookingCancellationFeedback? feedback = null)
    {
        ArgumentNullException.ThrowIfNull(booking);

        return new BookingCancellationPolicyInput(
            booking.Status,
            actorSide,
            action,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            cancelledOnUtc,
            ResolveCaptureEvidence(booking),
            feedback);
    }

    /// <summary>
    /// Reads capture evidence off a persisted booking without calling a provider.
    /// <para>
    /// A succeeded payment transaction is capture evidence on its own: a captured payment that lacks a
    /// provider transaction reference is still captured, and settling that gap is an application
    /// concern, never a reason to report "no refund due". When no transaction is succeeded, the legacy
    /// booking markers (<c>PaidOnUtc</c>, <c>PaymobTransactionId</c>) are consulted so that incomplete
    /// capture history on a paid booking becomes <see cref="BookingCaptureEvidence.Ambiguous"/> — which
    /// the policy refuses to decide — instead of being read as proof that nothing was ever paid.
    /// </para>
    /// </summary>
    public static BookingCaptureEvidence ResolveCaptureEvidence(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        bool hasSucceededTransaction = booking.PaymentTransactions
            .Any(transaction => transaction.Status == PaymentTransactionStatus.Succeeded);

        if (hasSucceededTransaction)
            return BookingCaptureEvidence.Captured;

        bool hasLegacyPaidEvidence =
            booking.PaidOnUtc is not null
            || !string.IsNullOrWhiteSpace(booking.PaymobTransactionId);

        return hasLegacyPaidEvidence
            ? BookingCaptureEvidence.Ambiguous
            : BookingCaptureEvidence.NotCaptured;
    }
}
