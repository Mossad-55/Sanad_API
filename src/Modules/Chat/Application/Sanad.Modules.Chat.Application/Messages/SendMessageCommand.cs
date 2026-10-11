using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Attachments;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.Modules.Chat.Application.Messages;

/// <summary>
/// Endpoint 4 — send one message into a conversation the requester
/// participates in. <c>Type</c> is the raw wire value ("Text"/"File") and
/// <c>ClientMessageId</c> is the caller's idempotency key, unique per
/// conversation; both are validated by the handler so a caller's own
/// malformed body answers <see cref="MessageErrors.Invalid"/> (400).
/// </summary>
public sealed record SendMessageCommand(
    UserId Requester,
    ConversationId ConversationId,
    string? ClientMessageId,
    string? Type,
    string? Text,
    AttachmentId? AttachmentId)
    : ICommand<SendMessageResponse>;

public sealed class SendMessageCommandHandler
    : ICommandHandler<SendMessageCommand, SendMessageResponse>
{
    private readonly IChatDbContext _dbContext;

    /// <summary>
    /// Seam to the Notifications module (the same <c>ISender</c> every
    /// controller already uses). Optional only so the pre-existing focused
    /// tests that construct this handler with the bare context keep
    /// compiling; the container always injects it, so every send in the
    /// running app fires the new-message alert.
    /// </summary>
    private readonly ISender? _notificationSender;

    public SendMessageCommandHandler(
        IChatDbContext dbContext,
        ISender? notificationSender = null)
    {
        _dbContext = dbContext;
        _notificationSender = notificationSender;
    }

    public async Task<Result<SendMessageResponse>> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        // Participant authorization first: unknown, foreign, and
        // hidden-for-requester conversations answer with the same privacy 404
        // (Chat.Conversation.NotFound) so no caller can probe which ids exist
        // or whether a conversation was hidden from them. Reopening
        // (endpoint 2) restores visibility.
        if (conversation is null ||
            !conversation.IsParticipant(request.Requester))
        {
            return Result<SendMessageResponse>.Failure(
                ConversationErrors.NotFound);
        }

        if (conversation.IsHiddenFor(request.Requester))
        {
            return Result<SendMessageResponse>.Failure(
                ConversationErrors.NotFound);
        }

        UserId recipient =
            conversation.ParticipantOne == request.Requester
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

        // Send-time block enforcement. Only "the recipient blocked the
        // sender" denies the send; the direction matters, so a sender who
        // blocked the recipient (the blocker) may still send. The failure is
        // deliberately the SAME generic privacy 404 as a non-participant —
        // never a block code — so the blocked sender cannot learn that a
        // block row exists. Unblock (endpoint 9) removes the row and is the
        // only state this check consults.
        bool blockedByRecipient = await _dbContext.Blocks.AnyAsync(
            block =>
                block.BlockedUserId == request.Requester &&
                block.BlockerUserId == recipient,
            cancellationToken);

        if (blockedByRecipient)
        {
            return Result<SendMessageResponse>.Failure(
                ConversationErrors.NotFound);
        }

        if (!TryParseType(request.Type, out MessageType type))
        {
            return Result<SendMessageResponse>.Failure(
                MessageErrors.Invalid);
        }

        string? clientMessageId = request.ClientMessageId?.Trim();

        if (string.IsNullOrEmpty(clientMessageId) ||
            clientMessageId.Length > Message.MaximumClientMessageIdLength)
        {
            return Result<SendMessageResponse>.Failure(
                MessageErrors.Invalid);
        }

        string? text;

        if (type == MessageType.Text)
        {
            // A Text message never carries an attachment reference.
            if (request.AttachmentId is not null)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            string candidate = (request.Text ?? string.Empty).Trim();

            if (candidate.Length == 0 ||
                candidate.Length > Message.MaximumTextLength)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            text = candidate;
        }
        else
        {
            // A File message never carries a body (the domain rejects it
            // too) and must name an attachment; an explicit empty guid is a
            // malformed body, not an unknown one.
            if (request.Text is not null ||
                request.AttachmentId is null ||
                request.AttachmentId.Value.Value == Guid.Empty)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            text = null;
        }

        // Idempotency runs BEFORE the attachment is resolved: an exact
        // replay must return the original even though its attachment is
        // already bound by that very message, and must never re-bind it.
        Message? existing = await _dbContext.Messages
            .SingleOrDefaultAsync(
                item =>
                    item.ConversationId == conversation.Id &&
                    item.ClientMessageId == clientMessageId,
                cancellationToken);

        if (existing is not null)
        {
            return IsExactReplay(existing, type, text, request.AttachmentId)
                ? Result<SendMessageResponse>.Success(
                    new SendMessageResponse(
                        await CreateItemAsync(
                            existing,
                            conversation,
                            request.Requester,
                            recipient,
                            cancellationToken),
                        Created: false))
                : Result<SendMessageResponse>.Failure(
                    MessageErrors.IdempotencyConflict);
        }

        Attachment? attachment = null;

        if (type == MessageType.File)
        {
            AttachmentId attachmentId = request.AttachmentId!.Value;

            attachment = await _dbContext.Attachments
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == attachmentId &&
                        item.ConversationId == conversation.Id,
                    cancellationToken);

            // Unknown id, another conversation's attachment, or another
            // participant's upload are indistinguishable 404s — the sender
            // may only ever bind an attachment of this conversation that
            // they uploaded themselves.
            if (attachment is null ||
                attachment.UploadedBy != request.Requester)
            {
                return Result<SendMessageResponse>.Failure(
                    AttachmentErrors.NotFound);
            }

            if (attachment.IsBound)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }
        }

        DateTime now = DateTime.UtcNow;
        Message message;

        try
        {
            message = Message.Create(
                conversation.Id,
                request.Requester,
                type,
                text,
                request.AttachmentId,
                clientMessageId,
                now);
        }
        catch (DomainException)
        {
            // Backstop for any invariant the pre-checks above did not name —
            // a caller mistake must never surface as a 500.
            return Result<SendMessageResponse>.Failure(
                MessageErrors.Invalid);
        }

        try
        {
            attachment?.Bind();
        }
        catch (DomainException)
        {
            return Result<SendMessageResponse>.Failure(
                MessageErrors.Invalid);
        }

        // Sending is the only thing that moves conversation activity.
        conversation.UpdateLastActivity(now);

        _dbContext.Messages.Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Lane B — new-message in-app alert through the existing
        // notification seam (frozen contract: conversation id + message id
        // only, never the text, which this call does not carry at all).
        // The producer is awaited, not fire-and-forget, and its failure is
        // deliberately NOT swallowed: the message row is already committed,
        // so a caller's retry with the same clientMessageId takes the exact
        // replay path above — 200, no duplicate message, no second producer
        // attempt — exactly the idempotent contract the send itself
        // follows. The residual is documented: a producer failure can leave
        // the message delivered without an alert (no retry loop exists for
        // this seam in V1). The replay path never re-fires the producer
        // because an existing message means its alert was already produced,
        // and the producer's per-(message, recipient) idempotency key would
        // dedupe it anyway.
        if (_notificationSender is not null)
        {
            await _notificationSender.Send(
                new CreateChatMessageNotificationCommand(
                    recipient.Value,
                    conversation.Id.Value,
                    message.Id.Value,
                    now),
                cancellationToken);
        }

        // A message can never come back read on its own create: no existing
        // watermark can cover something newer than every mark already taken,
        // so the tick is null without loading read state on the hot path.
        return Result<SendMessageResponse>.Success(
            new SendMessageResponse(
                MessageItemFactory.Create(message, readOnUtc: null),
                Created: true));
    }

    /// <summary>
    /// Exact replay means the same key with the same content: type, body
    /// (already trimmed, so the comparison is on what was stored) and
    /// attachment reference. Anything else with the same key is the frozen
    /// 409 idempotency conflict.
    /// </summary>
    private static bool IsExactReplay(
        Message existing,
        MessageType type,
        string? text,
        AttachmentId? attachmentId) =>
        existing.Type == type &&
        existing.Text == text &&
        existing.AttachmentId == attachmentId;

    /// <summary>
    /// Builds the item of a replayed message with the same read tick rule as
    /// the list: the reader of this message is whoever did not send it. A
    /// new message (create path) never needs this.
    /// </summary>
    private async Task<MessageItemResponse> CreateItemAsync(
        Message message,
        Conversation conversation,
        UserId requester,
        UserId recipient,
        CancellationToken cancellationToken)
    {
        UserId reader = message.SenderUserId == requester
            ? recipient
            : requester;

        IReadOnlyDictionary<UserId, ReadMark> marks =
            await MessageReadRules.LoadMarksAsync(
                _dbContext,
                conversation.Id,
                [requester, recipient],
                cancellationToken);

        ReadMark readerMark = marks.TryGetValue(reader, out ReadMark mark)
            ? mark
            : ReadMark.None;

        return MessageItemFactory.Create(
            message,
            MessageReadRules.ReadStamp(message, readerMark));
    }

    /// <summary>
    /// <c>type</c> is <c>Text</c> or <c>File</c>, matched by name (not by
    /// enum number, so a numeric string is never accepted as a type).
    /// </summary>
    private static bool TryParseType(
        string? value,
        out MessageType type)
    {
        string? trimmed = value?.Trim();

        if (string.Equals(
                trimmed, "Text", StringComparison.OrdinalIgnoreCase))
        {
            type = MessageType.Text;
            return true;
        }

        if (string.Equals(
                trimmed, "File", StringComparison.OrdinalIgnoreCase))
        {
            type = MessageType.File;
            return true;
        }

        type = default;
        return false;
    }
}
