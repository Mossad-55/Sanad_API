namespace Sanad.API.Controllers.Requests;

// Endpoint 4 — send a message. No body validation beyond model binding: the
// handler owns every rule (type must be Text/File, clientMessageId shape,
// text length, payload separation by type) so a caller's own malformed body
// answers 400 Chat.Message.Invalid, while unknown, foreign, hidden, and
// blocked conversations all collapse into the same generic privacy 404
// (Chat.Conversation.NotFound) and a reused clientMessageId with different
// content answers 409 Chat.Message.IdempotencyConflict.
public sealed record ChatSendMessageRequest(
    string? ClientMessageId,
    string? Type,
    string? Text,
    Guid? AttachmentId);
