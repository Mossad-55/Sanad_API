using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.Modules.Caregivers.Application.Availability;

namespace Ahlena.Modules.Caregivers.Application.Onboarding;

/// <summary>
/// Resolves the current calendar date in the documented caregiver display zone
/// (Africa/Cairo) from the injected UTC clock. Certificate expiry is a
/// calendar-day judgement in that zone, never a server-local or UTC-date guess.
/// </summary>
public static class CaregiverCertificateClock
{
    /// <summary>The Cairo calendar date for the injected clock's instant.</summary>
    public static DateOnly CairoDate(IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return ToCairoDate(clock.UtcNow);
    }

    /// <summary>
    /// Converts a UTC instant to its Africa/Cairo calendar date (real DST rules
    /// via <see cref="CaregiverAvailabilityTimeZone.Zone"/>).
    /// </summary>
    public static DateOnly ToCairoDate(DateTime utcNow) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                utcNow,
                CaregiverAvailabilityTimeZone.Zone));
}
