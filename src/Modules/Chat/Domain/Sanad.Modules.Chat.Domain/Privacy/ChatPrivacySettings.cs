using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Domain.Privacy;

/// <summary>
/// Per-user presence privacy settings. Both flags default to false for new
/// and legacy users (absence of a row means undisclosed); only liveness and
/// last-seen are ephemeral, these settings are durable. Managed by the
/// authenticated owner only; absence of a policy is never consent.
/// </summary>
public sealed class ChatPrivacySettings : AggregateRoot<ChatPrivacySettingsId>
{
    private ChatPrivacySettings()
    {
    }

    private ChatPrivacySettings(
        ChatPrivacySettingsId id,
        UserId ownerUserId,
        bool showOnlineStatus,
        bool shareLastSeen,
        DateTime updatedOnUtc)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        ShowOnlineStatus = showOnlineStatus;
        ShareLastSeen = shareLastSeen;
        UpdatedOnUtc = updatedOnUtc;
    }

    public UserId OwnerUserId { get; private set; } = default!;

    public bool ShowOnlineStatus { get; private set; }

    public bool ShareLastSeen { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static ChatPrivacySettings Create(
        UserId ownerUserId,
        bool showOnlineStatus,
        bool shareLastSeen,
        DateTime updatedOnUtc)
    {
        if (ownerUserId == UserId.Empty)
        {
            throw new DomainException(
                "Privacy settings owner is required.");
        }

        if (updatedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Privacy settings timestamps must be UTC.");
        }

        return new ChatPrivacySettings(
            ChatPrivacySettingsId.New(),
            ownerUserId,
            showOnlineStatus,
            shareLastSeen,
            updatedOnUtc);
    }

    public void Replace(
        bool showOnlineStatus,
        bool shareLastSeen,
        DateTime updatedOnUtc)
    {
        if (updatedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Privacy settings timestamps must be UTC.");
        }

        ShowOnlineStatus = showOnlineStatus;
        ShareLastSeen = shareLastSeen;
        UpdatedOnUtc = updatedOnUtc;
    }
}
