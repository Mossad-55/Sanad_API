using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Chat.Application.Conversations;

/// <summary>
/// Endpoint 7 failure codes. <see cref="Invalid"/> answers the caller's own
/// malformed body (an empty/missing <c>lastReadMessageId</c>) as a 400 — it
/// reveals nothing the caller does not already know, and it is never
/// reachable for someone who is not a participant: the handler runs the
/// conversation/participant gate first, so an unknown or foreign
/// conversation always answers <see cref="ConversationErrors.NotFound"/>
/// (forced into the same generic privacy 404 by the Chat controller).
///
/// Deliberately absent: any code for a message id that does not exist or is
/// not the newest. Lane A has no Message table to check against, and even
/// with one, telling the caller "that message id is wrong" would leak
/// conversation contents. Marks are recorded verbatim; Lane B interprets
/// them (unread counts, read ticks).
/// </summary>
public static class ConversationReadErrors
{
    public static readonly Error Invalid =
        new(
            "Chat.ConversationRead.Invalid",
            "The last read message reference is required.");
}
