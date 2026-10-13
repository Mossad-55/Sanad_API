using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Chat.Application.Attachments;

/// <summary>
/// Upload validation failures (Invalid, TooLarge, UnsupportedType) are
/// returned to the uploader as 400s — the caller knows their own file failed
/// validation and the specific code tells them why. <see cref="NotFound"/>
/// is different: on the serve path (endpoint 6) an unknown, foreign, or
/// cross-conversation attachment id must be indistinguishable, so the Chat
/// controller forces it into the same generic privacy 404 as
/// <see cref="Conversations.ConversationErrors.NotFound"/>. The shared
/// ResultProblemDetailsMapper maps unmapped codes to 400, which is correct
/// for the validation trio; NotFound never reaches it with its raw status
/// because the controller rewrites it.
/// </summary>
public static class AttachmentErrors
{
    public static readonly Error Invalid =
        new("Chat.Attachment.Invalid", "The uploaded attachment is invalid.");

    public static readonly Error TooLarge =
        new("Chat.Attachment.TooLarge", "The attachment exceeds the maximum size.");

    public static readonly Error UnsupportedType =
        new("Chat.Attachment.UnsupportedType", "The attachment type is not allowed.");

    public static readonly Error NotFound =
        new("Chat.Attachment.NotFound", "Attachment not found.");

    /// <summary>
    /// The caller's live unbound-upload quota (100 MiB default) is
    /// exhausted. Expired (24 h, default) unbound uploads do not count;
    /// bound history never counts and is never cleaned up.
    /// </summary>
    public static readonly Error QuotaExceeded =
        new("Chat.Attachment.QuotaExceeded", "The upload quota was exceeded.");
}
