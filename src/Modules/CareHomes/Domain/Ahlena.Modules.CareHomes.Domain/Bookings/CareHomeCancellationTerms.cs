using System.Text.Json;
using System.Text.Json.Serialization;
using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.CareHomes.Domain.Bookings;

/// <summary>
/// One deadline tier of a facility's structured cancellation terms: cancelling
/// at least <see cref="DeadlineHoursBeforeStart"/> hours before the scheduled
/// stay start refunds <see cref="RefundPercentage"/> percent of what was paid.
/// <para>
/// Contract clause 6 and decision #9: care-home custom terms must be
/// structured, displayed before booking and snapshotted; prose alone never
/// drives automatic money calculations. The percentages are whole numbers
/// expressed of the total captured amount; the final share is rounded half to
/// even by the policy, never here.
/// </para>
/// </summary>
public sealed record CareHomeCancellationTermTier
{
    public const int MinimumDeadlineHours = 0;
    public const int MaximumDeadlineHours = 8760;

    public CareHomeCancellationTermTier(int refundPercentage, int deadlineHoursBeforeStart)
    {
        if (refundPercentage is < 0 or > 100)
            throw new DomainException("A cancellation term refund percentage must be between 0 and 100.");
        if (deadlineHoursBeforeStart is < MinimumDeadlineHours or > MaximumDeadlineHours)
            throw new DomainException(
                $"A cancellation term deadline must be between {MinimumDeadlineHours} and {MaximumDeadlineHours} hours before the stay starts.");
        RefundPercentage = refundPercentage;
        DeadlineHoursBeforeStart = deadlineHoursBeforeStart;
    }

    /// <summary>Whole-number percentage (0–100) of the paid total refunded under this tier.</summary>
    [JsonPropertyName("refundPercentage")]
    public int RefundPercentage { get; }

    /// <summary>Whole hours before the scheduled stay start this tier's deadline sits at.</summary>
    [JsonPropertyName("deadlineHoursBeforeStart")]
    public int DeadlineHoursBeforeStart { get; }
}

/// <summary>
/// The structured cancellation terms a facility publishes on its profile and
/// that a booking snapshots at creation.
/// <para>
/// Tiers are stored normalized by deadline, longest first. A cancellation act
/// at instant T before the scheduled stay start resolves to the first tier
/// whose deadline it satisfies (<c>T &lt;= start − deadline</c>); when no tier
/// matches — including at or after the start — nothing is refunded. This
/// deliberately admits 0-percentage and total-percentage tiers so a facility
/// can express stricter or looser terms than the default schedule, while the
/// absence of terms entirely (a null snapshot) dispatches to the clause 2
/// default schedule in <see cref="CareHomeRefundPolicy"/>.
/// </para>
/// <para>
/// Serialization is a fixed camelCase shape persisted as JSON on the facility
/// profile revision (source of truth) and on the booking (snapshotted copy
/// with provenance), so a booking never reads "whatever the facility displays
/// today" (decision #10: facility changes apply only to future bookings).
/// </para>
/// </summary>
public sealed class CareHomeCancellationTerms : Ahlena.BuildingBlocks.Domain.Abstractions.ValueObject
{
    public const int MinimumTierCount = 1;
    public const int MaximumTierCount = 6;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private CareHomeCancellationTerms(IReadOnlyList<CareHomeCancellationTermTier> tiers)
    {
        Tiers = tiers;
    }

    /// <summary>Applicable tiers, longest deadline first (normalized order).</summary>
    public IReadOnlyList<CareHomeCancellationTermTier> Tiers { get; }

    /// <summary>
    /// Validates and normalizes the supplied tiers. Ties on a deadline are
    /// refused (an ambiguous tier cannot drive money), and the list is trimmed
    /// to the supported count so a profile can never smuggle in a wall of tiers.
    /// </summary>
    public static CareHomeCancellationTerms From(IReadOnlyList<CareHomeCancellationTermTier>? tiers)
    {
        if (tiers is null || tiers.Count == 0)
            throw new DomainException("Structured cancellation terms require at least one tier.");
        if (tiers.Count > MaximumTierCount)
            throw new DomainException(
                $"Structured cancellation terms are limited to {MaximumTierCount} tiers.");

        List<CareHomeCancellationTermTier> normalized = [];
        foreach (CareHomeCancellationTermTier tier in tiers)
        {
            if (normalized.Any(existing => existing.DeadlineHoursBeforeStart == tier.DeadlineHoursBeforeStart))
                throw new DomainException("A cancellation terms deadline may not repeat.");
            normalized.Add(tier);
        }

        return new([.. normalized.OrderByDescending(tier => tier.DeadlineHoursBeforeStart)]);
    }

    /// <summary>
    /// The share (0–1) of the paid total refunded by cancelling at
    /// <paramref name="actedOnUtc"/> for a stay whose scheduled start is
    /// <paramref name="scheduledStartUtc"/>. Both instants must be UTC. A
    /// cancellation at or after the start always resolves to zero: facilities
    /// may publish looser tiers only before the stay starts.
    /// </summary>
    public decimal ShareFor(DateTime scheduledStartUtc, DateTime actedOnUtc)
    {
        EnsureUtc(scheduledStartUtc, nameof(scheduledStartUtc));
        EnsureUtc(actedOnUtc, nameof(actedOnUtc));
        if (actedOnUtc >= scheduledStartUtc)
            return 0m;

        foreach (CareHomeCancellationTermTier tier in Tiers)
        {
            if (actedOnUtc <= scheduledStartUtc.AddHours(-tier.DeadlineHoursBeforeStart))
                return tier.RefundPercentage / 100m;
        }

        return 0m;
    }

    /// <summary>
    /// Parses persisted terms JSON. Null or whitespace means "no structured
    /// terms published" and yields null (the default schedule governs);
    /// anything present must parse cleanly and validate — a stored snapshot
    /// that cannot be re-read is an integrity failure, never silently a
    /// fallback.
    /// </summary>
    public static CareHomeCancellationTerms? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        List<CareHomeCancellationTermTier>? tiers;
        try
        {
            tiers = JsonSerializer.Deserialize<List<CareHomeCancellationTermTier>>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new DomainException(
                "The stored care-home cancellation terms could not be read. " + exception.Message);
        }

        return From(tiers);
    }

    /// <summary>
    /// Renders these terms to the persisted JSON shape used by both the
    /// profile revision (source) and the booking snapshot (copy). The output
    /// is deterministic for equal terms, byte for byte.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(Tiers, SerializerOptions);

    private static void EnsureUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"{field} must be a UTC timestamp.");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (CareHomeCancellationTermTier tier in Tiers)
        {
            yield return tier.RefundPercentage;
            yield return tier.DeadlineHoursBeforeStart;
        }
    }

    public override string ToString() => $"{Tiers.Count} cancellation term tier(s)";
}
