using System.Globalization;

namespace Ahlena.Modules.Finance.Application;

/// <summary>
/// Provisional cap for a single reviewed complaint award (clause 5, Owner
/// decision #12: SuperAdmin approves a reviewed financial award).
/// <para>
/// The execution contract initially deferred the award amount cap. With the
/// complaint-review surface live in Production, the Owner ordered a
/// provisional default enabled on 2026-10-23, to be confirmed or adjusted:
/// <see cref="DefaultMaxAwardMinorUnits"/>, i.e. EGP 500.00. The value is
/// configuration, not code: <c>Finance:ComplaintReviews:MaxAwardMinorUnits</c>.
/// </para>
/// <para>
/// Configuration semantics: a missing configuration key selects the
/// provisional default; an explicit value of <c>0</c> removes the cap; any
/// positive value replaces it. Negative and malformed configured values are
/// rejected at startup. The cap is an
/// application-seam guard on <c>ApproveComplaintReviewCommand</c> — it never
/// changes the reviewed state machine, the ledger append, or the domain
/// aggregate (the domain amount guard <c>AmountInvalid</c> only refuses
/// negative amounts).
/// </para>
/// </summary>
public sealed class ComplaintAwardOptions
{
    /// <summary>EGP 500.00 in piastres (Owner decision #4 minor-unit scale).</summary>
    public const long DefaultMaxAwardMinorUnits = 50_000;

    /// <summary>
    /// Longest permitted single reviewed complaint award in minor units
    /// (piastres); <c>null</c> means uncapped (explicitly disabled by
    /// configuration only — never the shipping default).
    /// </summary>
    public long? MaxAwardMinorUnits { get; init; } = DefaultMaxAwardMinorUnits;

    /// <summary>
    /// Builds the options from an already-parsed configuration value.
    /// <paramref name="configuredMaxAwardMinorUnits"/> is the parsed
    /// <c>Finance:ComplaintReviews:MaxAwardMinorUnits</c> setting, or
    /// <c>null</c> when absent; <c>null</c> selects the provisional default
    /// and zero explicitly disables the cap.
    /// <para>
    /// The production path calls <see cref="ParseConfiguredValue"/> first, so a
    /// negative raw value is rejected before reaching this factory. This factory
    /// keeps its historical <c>&lt;= 0</c> direct-call behavior (non-positive
    /// disables) only so existing callers/tests that pre-parse values remain
    /// stable; negative configuration never reaches it in DI.
    /// </para>
    /// </summary>
    public static ComplaintAwardOptions FromConfiguredValue(long? configuredMaxAwardMinorUnits) =>
        configuredMaxAwardMinorUnits is null
            ? new()
            : configuredMaxAwardMinorUnits <= 0
                ? new() { MaxAwardMinorUnits = null }
                : new() { MaxAwardMinorUnits = configuredMaxAwardMinorUnits };

    /// <summary>
    /// Parses the optional configuration value without treating malformed input
    /// as an absent setting (which would silently restore the default cap).
    /// </summary>
    public static long? ParseConfiguredValue(string? configuredValue)
    {
        if (configuredValue is null)
            return null;

        if (!long.TryParse(
                configuredValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long parsedValue))
        {
            throw new InvalidOperationException(
                "Finance:ComplaintReviews:MaxAwardMinorUnits must be a non-negative integer.");
        }

        return parsedValue;
    }
}
