using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Abstractions.Data;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.Chat.Application.Conversations;

namespace Ahlena.API.ChatIntegration;

/// <summary>
/// Facility presentation for care-home owner participants (Opus delta):
/// resolves the facility a user owns and returns its approved-revision name
/// (English preferred, Arabic fallback — the same preference
/// ApiChatUserLookup applies to person names) plus the approved cover image
/// URL. Null when the user owns no facility, the facility has no approved
/// revision, or no cover was uploaded — the caller then keeps the owner's
/// personal profile as the participant display. The bilingual fields carry
/// both approved-revision names as stored (C1); a blank one stays null.
/// </summary>
public sealed class ApiChatFacilityLookup : IChatFacilityLookup
{
    private readonly ICareHomesDbContext _careHomes;

    public ApiChatFacilityLookup(ICareHomesDbContext careHomes)
    {
        _careHomes = careHomes;
    }

    public async Task<FacilityPresentation?> GetForOwnerAsync(
        UserId ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId == UserId.Empty)
        {
            return null;
        }

        CareHomeFacility? facility = await _careHomes.Facilities
            .AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .Include(item => item.Revisions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (facility is null ||
            facility.ApprovedRevisionId is not Guid approvedRevisionId)
        {
            return null;
        }

        CareHomeProfileRevision? revision =
            facility.Revisions.FirstOrDefault(item =>
                item.Id == approvedRevisionId &&
                item.ApprovedOnUtc is not null);

        if (revision is null)
        {
            return null;
        }

        string displayName =
            string.IsNullOrWhiteSpace(revision.EnglishName)
                ? revision.ArabicName
                : revision.EnglishName;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        Guid? coverMediaId = await _careHomes.ProfileMedia
            .AsNoTracking()
            .Where(item =>
                item.ProfileRevisionId == revision.Id &&
                item.Kind == CareHomeProfileMediaKind.Cover)
            .OrderBy(item => item.Position)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        string? coverUrl = coverMediaId is { } mediaId
            ? $"/api/v1/care-homes/discovery/{facility.Id.Value:D}/media/{mediaId:D}"
            : null;

        // Bilingual names (C1): the approved revision's stored names; a blank
        // stored name stays null so the client falls back to displayName.
        return new FacilityPresentation(
            displayName,
            coverUrl,
            NullIfBlank(revision.ArabicName),
            NullIfBlank(revision.EnglishName));
    }

    /// <summary>
    /// True batch: one facility round-trip plus one cover round-trip per
    /// conversation-list page instead of per conversation.
    /// </summary>
    public async Task<IReadOnlyDictionary<UserId, FacilityPresentation>> GetForOwnersAsync(
        IReadOnlyCollection<UserId> ownerUserIds,
        CancellationToken cancellationToken = default)
    {
        UserId[] distinct = ownerUserIds
            .Where(id => id != UserId.Empty)
            .Distinct()
            .ToArray();

        var presentations = new Dictionary<UserId, FacilityPresentation>();

        if (distinct.Length == 0)
        {
            return presentations;
        }

        List<CareHomeFacility> facilities = await _careHomes.Facilities
            .AsNoTracking()
            .Where(item => distinct.Contains(item.OwnerUserId))
            .Include(item => item.Revisions)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var revisionIds = new List<Guid>();

        foreach (CareHomeFacility facility in facilities)
        {
            if (facility.ApprovedRevisionId is not Guid approvedRevisionId)
            {
                continue;
            }

            CareHomeProfileRevision? revision =
                facility.Revisions.FirstOrDefault(item =>
                    item.Id == approvedRevisionId &&
                    item.ApprovedOnUtc is not null);

            if (revision is null)
            {
                continue;
            }

            string displayName =
                string.IsNullOrWhiteSpace(revision.EnglishName)
                    ? revision.ArabicName
                    : revision.EnglishName;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            presentations[facility.OwnerUserId] = new FacilityPresentation(
                displayName,
                null,
                NullIfBlank(revision.ArabicName),
                NullIfBlank(revision.EnglishName));
            revisionIds.Add(revision.Id);
        }

        if (revisionIds.Count == 0)
        {
            return presentations;
        }

        var covers = await _careHomes.ProfileMedia
            .AsNoTracking()
            .Where(item =>
                revisionIds.Contains(item.ProfileRevisionId) &&
                item.Kind == CareHomeProfileMediaKind.Cover)
            .GroupBy(item => item.ProfileRevisionId)
            .Select(group => new
            {
                RevisionId = group.Key,
                MediaId = group.OrderBy(item => item.Position).Select(item => item.Id).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Re-resolve facility ids for cover URLs (in memory; page-bounded).
        foreach (var cover in covers)
        {
            if (cover.MediaId == Guid.Empty)
            {
                continue;
            }

            foreach (CareHomeFacility facility in facilities)
            {
                if (facility.ApprovedRevisionId != cover.RevisionId)
                {
                    continue;
                }

                if (!presentations.TryGetValue(facility.OwnerUserId, out FacilityPresentation? current))
                {
                    continue;
                }

                presentations[facility.OwnerUserId] = current with
                {
                    CoverImageUrl = $"/api/v1/care-homes/discovery/{facility.Id.Value:D}/media/{cover.MediaId:D}"
                };
            }
        }

        return presentations;
    }

    /// <summary>An absent or blank stored name is null, never an empty string.</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
