using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Display data for one conversation participant. Outbound port implemented
/// by Sanad.API over the Identity module (the same gateway pattern as
/// IFamilyIdentityGateway): the Chat Application references no Identity
/// types, so handlers and tests fake this port.
/// </summary>
public sealed record UserProfile(
    UserId UserId,
    string DisplayName,
    string? AvatarUrl);

public interface IChatUserLookup
{
    /// <summary>
    /// Resolves a single participant profile, or null when the user is
    /// unknown (never registered or already removed).
    /// </summary>
    Task<UserProfile?> GetProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default);
}
