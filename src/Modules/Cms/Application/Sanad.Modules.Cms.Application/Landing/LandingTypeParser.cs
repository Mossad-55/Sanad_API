using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Application.Landing;

/// <summary>
/// Parses admin-supplied section type keys (kebab-case registry keys or enum
/// names, case-insensitive). The virtual stats key is never accepted.
/// </summary>
public static class LandingTypeParser
{
    public static bool TryParseKey(string? value, out LandingSectionType type)
    {
        type = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim().ToLowerInvariant();
        foreach (LandingSectionType candidate in Enum.GetValues<LandingSectionType>())
        {
            if (string.Equals(candidate.ToTypeKey(), normalized, StringComparison.Ordinal))
            {
                type = candidate;
                return type != LandingSectionType.Stats;
            }
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out LandingSectionType parsed) &&
            Enum.IsDefined(parsed))
        {
            type = parsed;
            return type != LandingSectionType.Stats;
        }

        return false;
    }
}
