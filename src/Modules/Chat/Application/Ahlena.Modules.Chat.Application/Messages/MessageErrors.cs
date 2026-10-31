using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Chat.Application.Messages;

/// <summary>
/// Lane B message failure codes (endpoints 3, 4, and 8).
/// <para>
/// <see cref="Invalid"/> answers the caller's own malformed payload (unknown
/// <c>type</c>, empty/oversized text, empty/oversized <c>clientMessageId</c>,
/// a Text message carrying an <c>attachmentId</c>, a File message without
/// one or carrying a body, an empty attachment guid, or an attachment that is
/// already bound) as a 400 — it reveals nothing the sender does not already
/// know about their own request.
/// </para>
/// <para>
/// <see cref="IdempotencyConflict"/> means the per-conversation
/// <c>clientMessageId</c> already exists with different content; the Chat
/// controller forces it to 409 here rather than editing the shared
/// ResultProblemDetailsMapper (which has no Chat.* entries and defaults
/// unmapped codes to 400).
/// </para>
/// <para>
/// <see cref="NotFound"/> is the message-scoped privacy 404: an unresolvable
/// <c>beforeMessageId</c> cursor answers with it, and the controller forces
/// the same generic "resource not found" detail as an unknown conversation,
/// so nothing about ids outside the caller's own conversation ever leaks.
/// Unknown/foreign conversation ids stay on
/// <see cref="Conversations.ConversationErrors.NotFound"/> and unknown/foreign
/// attachment ids stay on <see cref="Attachments.AttachmentErrors.NotFound"/>,
/// reusing the existing vocabulary instead of duplicating it.
/// </para>
/// </summary>
public static class MessageErrors
{
    public static readonly Error Invalid =
        new("Chat.Message.Invalid", "The message is invalid.");

    public static readonly Error IdempotencyConflict =
        new(
            "Chat.Message.IdempotencyConflict",
            "A different message already uses this client reference.");

    public static readonly Error NotFound =
        new("Chat.Message.NotFound", "Message not found.");
}
