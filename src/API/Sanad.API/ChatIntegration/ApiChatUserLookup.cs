using MediatR;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.ChatIntegration;

/// <summary>
/// API-layer gateway: wraps Identity's GetUserProfilesByIdsQuery so the
/// Chat Application only sees <see cref="IChatUserLookup"/> (mirrors
/// FamilyIdentityGateway). displayName prefers the English full name and
/// falls back to Arabic when it is empty.
/// </summary>
public sealed class ApiChatUserLookup : IChatUserLookup
{
    private readonly ISender _sender;

    public ApiChatUserLookup(ISender sender)
    {
        _sender = sender;
    }

    public async Task<UserProfile?> GetProfileAsync(
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == UserId.Empty)
        {
            return null;
        }

        var result = await _sender.Send(
            new GetUserProfilesByIdsQuery([userId]),
            cancellationToken);

        if (result.IsFailure)
        {
            return null;
        }

        UserProfileById? profile = result.Value.FirstOrDefault();

        if (profile is null)
        {
            return null;
        }

        string displayName =
            string.IsNullOrWhiteSpace(profile.EnglishFullName)
                ? profile.ArabicFullName
                : profile.EnglishFullName;

        return new UserProfile(
            profile.UserId,
            displayName,
            profile.AvatarUrl,
            profile.AccountType);
    }
}
