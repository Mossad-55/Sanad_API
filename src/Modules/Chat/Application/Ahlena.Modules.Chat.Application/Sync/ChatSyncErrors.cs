using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Chat.Application.Sync;

/// <summary>
/// Sync failure codes. A malformed, foreign, requester-mismatched, or
/// undecryptable cursor is the generic privacy 404 (never an existence or
/// cursor oracle); an authentic but no-longer-supported cursor version
/// returns an empty page with `resetRequired: true`.
/// </summary>
public static class ChatSyncErrors
{
    public static readonly Error NotFound =
        new("Chat.Sync.NotFound", "Conversation not found.");

    public static readonly Error InvalidPageSize =
        new("Chat.Sync.InvalidPageSize", "The page size is invalid.");
}
