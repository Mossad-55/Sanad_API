using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Elderlies;

public static class ElderlyTimeZone
{
    public const string InitialDefaultId = "Africa/Cairo";
    public const int MaximumIdLength = 100;

    public static string Normalize(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("An IANA time zone is required.");

        string normalized = timeZoneId.Trim();
        if (normalized.Length > MaximumIdLength)
            throw new DomainException("Time zone ID is too long.");
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(normalized, out _))
            throw new DomainException("Time zone ID must use an IANA identifier.");

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new DomainException("Time zone ID is invalid.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new DomainException("Time zone ID is invalid.");
        }

        return normalized;
    }

    public static bool IsValid(string? timeZoneId)
    {
        try
        {
            _ = Normalize(timeZoneId);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}
