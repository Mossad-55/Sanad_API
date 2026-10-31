namespace Ahlena.Modules.Caregivers.Application.Availability;

/// <summary>
/// The documented display zone for caregiver availability. The stored zone
/// semantics of the owning domain stay authoritative for anything persisted;
/// this type only resolves the IANA id (with the Windows equivalent as
/// fallback) when declared weekly windows are read as instants.
/// </summary>
public static class CaregiverAvailabilityTimeZone
{
    public const string Id = "Africa/Cairo";

    private const string WindowsId = "Egypt Standard Time";

    /// <summary>The resolved zone, including its real daylight-saving rules.</summary>
    public static TimeZoneInfo Zone { get; } = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(Id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsId);
        }
    }
}
