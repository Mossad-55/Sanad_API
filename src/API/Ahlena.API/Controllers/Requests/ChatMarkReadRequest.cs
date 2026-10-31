namespace Ahlena.API.Controllers.Requests;

// Endpoint 7 — mark a conversation read. No body validation beyond model
// binding. The handler runs the conversation/participant gate first, so an
// unknown or foreign conversation id answers the generic privacy 404
// (Chat.Conversation.NotFound) whatever the body contains; only a
// participant with an empty/missing lastReadMessageId receives the 400
// (Chat.ConversationRead.Invalid). A non-empty id is recorded verbatim —
// Lane A has no Message table to validate it against.
public sealed record ChatMarkReadRequest(
    Guid LastReadMessageId);
