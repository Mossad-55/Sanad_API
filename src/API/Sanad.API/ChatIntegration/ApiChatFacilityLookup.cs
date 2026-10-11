using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Chat.Application.Conversations;

namespace Sanad.API.ChatIntegration;

/// <summary>
/// Facility presentation for care-home owner participants (Opus delta):
/// resolves the facility a user owns and returns its approved-revision name
/// (English preferred, Arabic fallback — the same preference
/// ApiChatUserLookup applies to person names) plus the approved cover image
/// URL. Null when the user owns no facility, the facility has no approved
/// revision, or no cover was uploaded — the caller then keeps the owner's
/// personal profile as the participant display.
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

        return new FacilityPresentation(displayName, coverUrl);
    }
}
