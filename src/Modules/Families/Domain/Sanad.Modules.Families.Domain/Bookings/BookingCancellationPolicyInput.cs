namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Everything the cancellation policy is allowed to look at. Times are authoritative values supplied
/// by the caller (server time provider and persisted booking timestamps); the policy never reads a
/// clock itself. Payment facts are supplied as facts, so the policy never queries a provider.
/// </summary>
/// <param name="Status">Persisted <see cref="Booking.Status"/> at the moment of cancellation.</param>
/// <param name="ActorSide">Who ends the booking.</param>
/// <param name="Action">Cancel, or a caregiver rejection of a booking awaiting approval.</param>
/// <param name="ConfirmedOnUtc">
/// Persisted acceptance time (<c>Booking.ConfirmedOnUtc</c>); null while the booking is unaccepted.
/// </param>
/// <param name="StartedOnUtc">Persisted visit start time (<c>Booking.StartedOnUtc</c>); normally null here.</param>
/// <param name="CancelledOnUtc">Authoritative UTC time of the cancellation act.</param>
/// <param name="HasCapturedPayment">True when a payment for this booking is captured and not yet refunded.</param>
/// <param name="Feedback">Reason category and note; mandatory once the booking was accepted.</param>
public sealed record BookingCancellationPolicyInput(
    BookingStatus Status,
    BookingCancellationActorSide ActorSide,
    BookingCancellationAction Action,
    DateTime? ConfirmedOnUtc,
    DateTime? StartedOnUtc,
    DateTime CancelledOnUtc,
    bool HasCapturedPayment,
    BookingCancellationFeedback? Feedback)
{
    public static BookingCancellationPolicyInput ForFamilyCancellation(
        BookingStatus status,
        DateTime? confirmedOnUtc,
        DateTime? startedOnUtc,
        DateTime cancelledOnUtc,
        bool hasCapturedPayment,
        BookingCancellationFeedback? feedback = null) =>
        new(
            status,
            BookingCancellationActorSide.Family,
            BookingCancellationAction.Cancel,
            confirmedOnUtc,
            startedOnUtc,
            cancelledOnUtc,
            hasCapturedPayment,
            feedback);

    public static BookingCancellationPolicyInput ForCaregiverRejection(
        BookingStatus status,
        DateTime cancelledOnUtc,
        bool hasCapturedPayment,
        BookingCancellationFeedback? feedback = null) =>
        new(
            status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Reject,
            null,
            null,
            cancelledOnUtc,
            hasCapturedPayment,
            feedback);

    public static BookingCancellationPolicyInput ForCaregiverCancellation(
        BookingStatus status,
        DateTime? confirmedOnUtc,
        DateTime? startedOnUtc,
        DateTime cancelledOnUtc,
        bool hasCapturedPayment,
        BookingCancellationFeedback? feedback = null) =>
        new(
            status,
            BookingCancellationActorSide.Caregiver,
            BookingCancellationAction.Cancel,
            confirmedOnUtc,
            startedOnUtc,
            cancelledOnUtc,
            hasCapturedPayment,
            feedback);

    /// <summary>
    /// Builds the input from a persisted booking and the caller's authoritative time. Capture is read
    /// from the booking's payment transactions; whether a gateway transaction id exists for the actual
    /// refund call stays an application concern and never changes this policy's answer.
    /// </summary>
    public static BookingCancellationPolicyInput FromBooking(
        Booking booking,
        BookingCancellationActorSide actorSide,
        BookingCancellationAction action,
        DateTime cancelledOnUtc,
        BookingCancellationFeedback? feedback = null)
    {
        ArgumentNullException.ThrowIfNull(booking);

        bool hasCapturedPayment = booking.PaymentTransactions
            .Any(transaction => transaction.Status == PaymentTransactionStatus.Succeeded);

        return new BookingCancellationPolicyInput(
            booking.Status,
            actorSide,
            action,
            booking.ConfirmedOnUtc,
            booking.StartedOnUtc,
            cancelledOnUtc,
            hasCapturedPayment,
            feedback);
    }
}
