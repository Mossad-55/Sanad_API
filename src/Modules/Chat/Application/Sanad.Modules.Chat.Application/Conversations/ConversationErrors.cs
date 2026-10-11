using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Lane A deliberately funnels every conversation failure through one code:
/// unknown, foreign, and ineligible must be indistinguishable to a caller,
/// so privacy-preserving 404s never leak the reason. The single exception is
/// <see cref="OpenTargetInvalid"/>: a malformed open body (both targets or
/// neither) is the caller's own request error and answers 400 — it leaks
/// nothing about any resource.
/// </summary>
public static class ConversationErrors
{
    public static readonly Error NotFound =
        new("Chat.Conversation.NotFound", "Conversation not found.");

    /// <summary>
    /// Endpoint 2 requires exactly one of <c>recipientUserId</c> (direct
    /// chat) or <c>bookingId</c> (booking chat); both or neither is a 400.
    /// </summary>
    public static readonly Error OpenTargetInvalid =
        new(
            "Chat.Conversation.OpenTargetInvalid",
            "Provide exactly one of recipientUserId or bookingId.");
}
