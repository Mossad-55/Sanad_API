namespace Sanad.API.Controllers.Requests;

// Endpoint 2 — open-or-get a conversation. No body validation beyond model
// binding: an unknown/empty recipient or booking reference is answered with
// the same privacy-preserving 404 as any ineligible open.
public sealed record OpenConversationRequest(
    Guid RecipientUserId,
    Guid? BookingId);
