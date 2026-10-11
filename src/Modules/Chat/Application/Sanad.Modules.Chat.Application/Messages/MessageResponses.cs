namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// The <c>attachment</c> object of a message item: only the reference the
/// client needs to call the file endpoint (endpoint 6). Metadata such as
/// content type and size travels with the file bytes themselves, so the
/// message list never joins the attachment row.
/// </summary>
public sealed record MessageAttachmentResponse(Guid AttachmentId);

/// <summary>
/// Frozen message item shape: <c>id</c>, <c>clientMessageId</c>,
/// <c>senderUserId</c>, <c>type</c> ("Text"/"File"), <c>text</c> (null for
/// File), <c>attachment</c>, <c>sentOnUtc</c>, <c>deliveredOnUtc</c>,
/// <c>readOnUtc</c>.
/// </summary>
public sealed record MessageItemResponse(
    Guid Id,
    string ClientMessageId,
    Guid SenderUserId,
    string Type,
    string? Text,
    MessageAttachmentResponse? Attachment,
    DateTime SentOnUtc,
    DateTime? DeliveredOnUtc,
    DateTime? ReadOnUtc);

/// <summary>
/// Cursor page for endpoint 3: newest first, <c>nextCursor</c> is the opaque
/// key of the last returned item (pass it back as <c>beforeMessageId</c>;
/// a raw message id works too) and is null once <c>hasMore</c> is false.
/// </summary>
public sealed record MessageCursorPage(
    IReadOnlyList<MessageItemResponse> Items,
    string? NextCursor,
    bool HasMore);

/// <summary>
/// Endpoint 4 result. <c>Created</c> is the send/create discriminator the
/// controller turns into 201 (new message) or 200 (exact replay), mirroring
/// how endpoint 2 reports open-or-get with <c>created</c>.
/// </summary>
public sealed record SendMessageResponse(
    MessageItemResponse Message,
    bool Created);

/// <summary>
/// Endpoint 8 result: unread = not-read (see <see cref="MessageReadRules"/>)
/// for the requester, over visible (non-hidden-for-requester) conversations.
/// </summary>
public sealed record UnreadCountResponse(
    int UnreadMessagesCount,
    int UnreadConversationsCount);
