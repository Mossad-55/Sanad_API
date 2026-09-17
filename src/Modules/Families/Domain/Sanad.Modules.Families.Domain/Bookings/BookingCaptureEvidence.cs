namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Capture evidence the caller supplies to the cancellation policy. It states what is known about
/// money taken for a booking; it is never a provider call and it never carries an amount.
/// <para>
/// Missing or contradictory capture history on a booking whose state says it was paid is reported as
/// <see cref="Ambiguous"/> so the policy can fail closed. Uncertainty is therefore never collapsed
/// into "no refund due".
/// </para>
/// </summary>
public enum BookingCaptureEvidence
{
    /// <summary>No payment evidence at all: the booking was genuinely never paid.</summary>
    NotCaptured = 1,

    /// <summary>Captured payment evidence exists for this booking.</summary>
    Captured = 2,

    /// <summary>
    /// Conflicting or incomplete evidence, for example a booking marked paid with no succeeded
    /// payment transaction. An integrity problem to resolve, not a refund answer.
    /// </summary>
    Ambiguous = 3
}
