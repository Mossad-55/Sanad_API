using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Chat.Application.Privacy;

/// <summary>Presence-privacy failure codes (owner-only endpoints).</summary>
public static class ChatPrivacyErrors
{
    public static readonly Error Invalid =
        new("Chat.Privacy.Invalid", "The privacy settings are invalid.");
}

/// <summary>
/// Owner privacy settings wire shape. PUT requires both booleans (full
/// replacement; omitted fields are not defaulted).
/// </summary>
public sealed record ChatPrivacyResponse(
    bool ShowOnlineStatus,
    bool ShareLastSeen);
