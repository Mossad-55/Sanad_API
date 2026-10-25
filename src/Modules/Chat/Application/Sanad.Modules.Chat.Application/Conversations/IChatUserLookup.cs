using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Display data for one conversation participant. Outbound port implemented
/// by Sanad.API over the Identity module (the same gateway pattern as
/// IFamilyIdentityGateway): the Chat Application references no Identity
/// types, so handlers and tests fake this port.
/// <c>AccountType</c> is the Identity <c>AccountType</c> integer (Opus
/// delta) — null only when the user row is gone. It is the
/// first-registered account and exists for DISPLAY only: eligibility uses
/// <c>CurrentAccountTypes</c> (every account on the current user row),
/// never this display integer.
/// <para>
/// Bilingual names (portal second pass C1): <c>DisplayNameArabic</c> and
/// <c>DisplayNameEnglish</c> carry the stored full names in each language
/// exactly as registered, or null when that language is absent. They are
/// additive display data only — <c>DisplayName</c> keeps its existing
/// resolution (English preferred, Arabic fallback) and drives nothing.
/// </para>
/// </summary>
public sealed record UserProfile(
    UserId UserId,
    string DisplayName,
    string? AvatarUrl,
    int? AccountType,
    IReadOnlyList<int>? CurrentAccountTypes = null,
    string? DisplayNameArabic = null,
    string? DisplayNameEnglish = null)
{
    /// <summary>Every current account type; falls back to the display
    /// integer when the lookup did not resolve the full set.</summary>
    public IReadOnlyList<int> EffectiveAccountTypes =>
        CurrentAccountTypes ?? (AccountType is { } single ? [single] : []);
}

public interface IChatUserLookup
{
    /// <summary>
    /// Resolves a single participant profile, or null when the user is
    /// unknown (never registered or already removed).
    /// </summary>
    Task<UserProfile?> GetProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched profile resolution for conversation lists (one Identity
    /// round-trip per page instead of per conversation). The default loops
    /// the single lookup; implementations override with a true batch.
    /// Every requested id gets an entry (null when unknown).
    /// </summary>
    async Task<IReadOnlyDictionary<UserId, UserProfile?>> GetProfilesAsync(
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken = default)
    {
        var profiles = new Dictionary<UserId, UserProfile?>();

        foreach (UserId userId in userIds.Distinct().ToArray())
        {
            profiles[userId] = await GetProfileAsync(userId, cancellationToken)
                .ConfigureAwait(false);
        }

        return profiles;
    }
}
