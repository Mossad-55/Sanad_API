using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Lane A deliberately funnels every conversation failure through one code:
/// unknown, foreign, and ineligible must be indistinguishable to a caller,
/// so privacy-preserving 404s never leak the reason.
/// </summary>
public static class ConversationErrors
{
    public static readonly Error NotFound =
        new("Chat.Conversation.NotFound", "Conversation not found.");
}
