namespace Sanad.Modules.CareHomes.Application.Bookings;

/// <summary>
/// Lifecycle durations used when creating and paying a care-home booking.
/// Production defaults are part of the approved booking contract. The API may
/// provide shorter values only from its explicitly enabled Development test clock.
/// </summary>
public sealed record CareHomeBookingTiming(
    TimeSpan CheckoutHoldDuration,
    TimeSpan DecisionHoldDuration,
    TimeSpan ExpirySweepInterval)
{
    public TimeSpan ArrivalLeadDuration => TimeSpan.FromHours(24);

    public static CareHomeBookingTiming Default { get; } = new(
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(24),
        TimeSpan.FromMinutes(1));
}
