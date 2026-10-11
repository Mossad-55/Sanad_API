namespace Sanad.API.Controllers.Requests;

// Endpoint 9 — block a user. No body validation beyond model binding: an
// empty/unknown blockedUserId is answered with the caller's own 400
// (Chat.Block.Invalid) or the generic privacy 404 (Chat.Block.NotFound)
// respectively; the caller never learns whether an arbitrary id is
// registered.
public sealed record BlockUserRequest(
    Guid BlockedUserId);
