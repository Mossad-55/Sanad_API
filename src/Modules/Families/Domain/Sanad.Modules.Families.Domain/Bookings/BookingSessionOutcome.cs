namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Whether the care a session represents was actually delivered.
/// <para>
/// This is the single fact that drives clause 3: delivered sessions are non-refundable, and only
/// undelivered sessions are refunded at their allocated discounted package price. It is recorded
/// per session rather than derived from the booking status, because a multi-session package can
/// have a delivered first session, a cancelled middle session and a still-future last session at
/// the same moment — the booking status cannot express that, but the money result depends on it.
/// </para>
/// <para>
/// Values start at 1 so that an unmapped or fabricated value is rejected instead of being read as
/// "not delivered" (which would silently turn into a refund).
/// </para>
/// </summary>
public enum BookingSessionOutcome
{
    /// <summary>The session is scheduled but not yet delivered. Refundable if cancelled in time.</summary>
    NotDelivered = 1,

    /// <summary>Clause 3: the session was delivered. Delivered sessions are non-refundable.</summary>
    Delivered = 2,

    /// <summary>
    /// The session was cancelled before delivery. Refundable on the same terms as an undelivered
    /// session: the refund depends on when it was cancelled, not on the fact of the cancellation.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// The provider failed to deliver the session (cancellation, no-show, excessive lateness).
    /// Refundable in full for that session under clause 4, regardless of the 24-hour window.
    /// </summary>
    ProviderFailed = 4
}
