namespace Sanad.API.Controllers.Requests;

// Chat Plus presence privacy (PUT /api/v1/chat/privacy): full replacement.
// Both booleans are required — nullable so an omitted JSON field is a 400
// (Chat.Privacy.Invalid), never a silent default.
public sealed record ChatUpdatePrivacyRequest(
    bool? ShowOnlineStatus,
    bool? ShareLastSeen);
