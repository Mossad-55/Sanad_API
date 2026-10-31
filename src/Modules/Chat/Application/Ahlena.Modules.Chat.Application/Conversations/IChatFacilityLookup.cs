using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Application.Conversations;

/// <summary>
/// Facility presentation for a care-home owner participant (Opus delta).
/// Implemented in Ahlena.API over the CareHomes store; the Chat Application
/// only declares the seam so it never takes a cross-module project
/// reference.
/// <para>
/// Bilingual names (portal second pass C1): <c>DisplayNameArabic</c> and
/// <c>DisplayNameEnglish</c> are the approved revision's stored names in
/// each language, or null when that language is blank. They are additive
/// display data only — <c>DisplayName</c> keeps its existing resolution
/// (English preferred, Arabic fallback) and drives nothing.
/// </para>
/// </summary>
public sealed record FacilityPresentation(
    string DisplayName,
    string? CoverImageUrl,
    string? DisplayNameArabic = null,
    string? DisplayNameEnglish = null);

public interface IChatFacilityLookup
{
    /// <summary>
    /// Resolves the facility a user owns, or null when the user owns no
    /// facility (or the facility has no approved revision to present).
    /// Callers use it to show the facility's name/cover instead of the
    /// owner's personal profile in participant surfaces.
    /// </summary>
    Task<FacilityPresentation?> GetForOwnerAsync(
        UserId ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched facility resolution for conversation lists. The default loops
    /// the single lookup; implementations override with a true batch. Only
    /// owners with a presentable facility get an entry.
    /// </summary>
    async Task<IReadOnlyDictionary<UserId, FacilityPresentation>> GetForOwnersAsync(
        IReadOnlyCollection<UserId> ownerUserIds,
        CancellationToken cancellationToken = default)
    {
        var facilities = new Dictionary<UserId, FacilityPresentation>();

        foreach (UserId ownerUserId in ownerUserIds.Distinct().ToArray())
        {
            FacilityPresentation? facility = await GetForOwnerAsync(ownerUserId, cancellationToken)
                .ConfigureAwait(false);

            if (facility is not null)
            {
                facilities[ownerUserId] = facility;
            }
        }

        return facilities;
    }
}
