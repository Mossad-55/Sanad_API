using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// Maps a persisted <see cref="Message"/> onto the frozen wire item shape
/// (endpoint 3/4). The attachment travels as its id only — file metadata
/// belongs to the file endpoint (endpoint 6), so the message item never
/// joins the attachment row. The location payload is carried verbatim for
/// <see cref="MessageType.Location"/> messages and stays null otherwise.
/// <c>readOnUtc</c> is not the entity's raw stamp: it is whatever
/// <see cref="MessageReadRules.ReadStamp"/> decides for the reader of this
/// item, so the tick and the unread counts always agree.
/// </summary>
internal static class MessageItemFactory
{
    public static MessageItemResponse Create(
        Message message,
        DateTime? readOnUtc) =>
        new(
            message.Id.Value,
            message.ConversationId.Value,
            message.ClientMessageId,
            message.SenderUserId.Value,
            message.Type.ToString(),
            message.Text,
            message.AttachmentId is { } attachmentId
                ? new MessageAttachmentResponse(attachmentId.Value)
                : null,
            message.Type == MessageType.Location &&
            message.Latitude is { } latitude &&
            message.Longitude is { } longitude
                ? new MessageLocationResponse(
                    latitude,
                    longitude,
                    message.LocationLabel)
                : null,
            message.SentOnUtc,
            message.DeliveredOnUtc,
            readOnUtc);
}
