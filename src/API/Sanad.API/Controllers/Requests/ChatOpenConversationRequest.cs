namespace Sanad.API.Controllers.Requests;

// Endpoint 2 — open-or-get a conversation. Exactly one target is required
// (Opus delta): recipientUserId opens a direct chat, bookingId opens a
// booking chat whose counterpart the backend resolves. Both or neither is
// 400 Chat.Conversation.OpenTargetInvalid; a target that resolves to nothing
// (unknown/empty recipient, unknown/ineligible/cancelled booking) keeps the
// same privacy-preserving 404 as any ineligible open.
public sealed record OpenConversationRequest(
    Guid? RecipientUserId,
    Guid? BookingId);
