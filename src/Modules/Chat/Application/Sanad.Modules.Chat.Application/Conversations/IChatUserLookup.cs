using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Display data for one conversation participant. Outbound port implemented
/// by Sanad.API over the Identity module (the same gateway pattern as
/// IFamilyIdentityGateway): the Chat Application references no Identity
/// types, so handlers and tests fake this port.
/// <c>AccountType</c> is the Identity <c>AccountType</c> integer (Opus
/// delta) — null only when the user row is gone.
/// </summary>
public sealed record UserProfile(
    UserId UserId,
    string DisplayName,
    string? AvatarUrl,
    int? AccountType);

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
