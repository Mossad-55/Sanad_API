using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Facility presentation for a care-home owner participant (Opus delta).
/// Implemented in Sanad.API over the CareHomes store; the Chat Application
/// only declares the seam so it never takes a cross-module project
/// reference.
/// </summary>
public sealed record FacilityPresentation(
    string DisplayName,
    string? CoverImageUrl);

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
}
