using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// Maps a persisted <see cref="Message"/> onto the frozen wire item shape
/// (endpoint 3/4). The attachment travels as its id only — file metadata
/// belongs to the file endpoint (endpoint 6), so the message item never
/// joins the attachment row. <c>readOnUtc</c> is not the entity's raw stamp:
/// it is whatever <see cref="MessageReadRules.ReadStamp"/> decides for the
/// reader of this item, so the tick and the unread counts always agree.
/// </summary>
internal static class MessageItemFactory
{
    public static MessageItemResponse Create(
        Message message,
        DateTime? readOnUtc) =>
        new(
            message.Id.Value,
            message.ClientMessageId,
            message.SenderUserId.Value,
            message.Type.ToString(),
            message.Text,
            message.AttachmentId is { } attachmentId
                ? new MessageAttachmentResponse(attachmentId.Value)
                : null,
            message.SentOnUtc,
            message.DeliveredOnUtc,
            readOnUtc);
}
