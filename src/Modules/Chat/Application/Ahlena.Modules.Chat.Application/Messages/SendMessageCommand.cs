using MediatR;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.Attachments;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Chat.Application.Events;
using Ahlena.Modules.Chat.Application.Persistence;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Changes;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Notifications.Application.Notifications;

namespace Ahlena.Modules.Chat.Application.Messages;

/// <summary>
/// Endpoint 4 — send one message into a conversation the requester
/// participates in. <c>Type</c> is the raw wire value ("Text"/"File"/
/// "Location") and <c>ClientMessageId</c> is the caller's idempotency key,
/// unique per conversation; both are validated by the handler so a caller's
/// own malformed body answers <see cref="MessageErrors.Invalid"/> (400).
/// <c>Latitude</c>/<c>Longitude</c>/<c>Label</c> form the Location payload
/// and must be absent for every other type.
/// </summary>
public sealed record SendMessageCommand(
    UserId Requester,
    ConversationId ConversationId,
    string? ClientMessageId,
    string? Type,
    string? Text,
    AttachmentId? AttachmentId,
    double? Latitude = null,
    double? Longitude = null,
    string? Label = null)
    : ICommand<SendMessageResponse>;

public sealed class SendMessageCommandHandler
    : ICommandHandler<SendMessageCommand, SendMessageResponse>
{
    private readonly IChatDbContext _dbContext;

    /// <summary>
    /// Seam to the Notifications module (the same <c>ISender</c> every
    /// controller already uses). Optional only so the pre-existing focused
    /// tests that construct this handler with the bare context keep
    /// compiling; the container always injects it. Used only when no durable
    /// recorder is wired (legacy path); with a recorder the dispatcher
    /// publishes the staged intent after commit instead.
    /// </summary>
    private readonly ISender? _notificationSender;
    private readonly IChatRateGate? _rateGate;
    private readonly IChatEventRecorder _recorder;
    private readonly TimeSpan _unboundLifetime;

    public SendMessageCommandHandler(
        IChatDbContext dbContext,
        ISender? notificationSender = null,
        IChatRateGate? rateGate = null,
        IChatEventRecorder? recorder = null,
        ChatRateOptions? rateOptions = null)
    {
        _dbContext = dbContext;
        _notificationSender = notificationSender;
        _rateGate = rateGate;
        _recorder = recorder ?? NullChatEventRecorder.Instance;
        _unboundLifetime = (rateOptions ?? new ChatRateOptions()).UnboundAttachmentLifetime;
    }

    public async Task<Result<SendMessageResponse>> Handle(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        if (_rateGate is not null &&
            !_rateGate.TryAcquireSend(
                request.Requester,
                request.ConversationId,
                out TimeSpan retryAfter))
        {
            return Result<SendMessageResponse>.Failure(
                ChatRateErrors.Sends(retryAfter));
        }

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
        double? latitude;
        double? longitude;
        string? label;

        if (type == MessageType.Text)
        {
            // A Text message never carries an attachment or location.
            if (request.AttachmentId is not null ||
                request.Latitude is not null ||
                request.Longitude is not null ||
                request.Label is not null)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            // Finite-number coordinate hygiene also applies to text: NaN and
            // infinities are never valid payloads anywhere on this route.
            string candidate = (request.Text ?? string.Empty).Trim();

            if (candidate.Length == 0 ||
                candidate.Length > Message.MaximumTextLength)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            text = candidate;
            latitude = null;
            longitude = null;
            label = null;
        }
        else if (type == MessageType.Location)
        {
            // A Location message carries exactly the coordinate pair plus
            // an optional label — no body, no attachment reference.
            // Coordinates must be finite numbers inside the valid ranges.
            if (request.Text is not null ||
                request.AttachmentId is not null ||
                request.Latitude is null ||
                request.Longitude is null ||
                double.IsNaN(request.Latitude.Value) ||
                double.IsInfinity(request.Latitude.Value) ||
                double.IsNaN(request.Longitude.Value) ||
                double.IsInfinity(request.Longitude.Value) ||
                request.Latitude is < -90 or > 90 ||
                request.Longitude is < -180 or > 180)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            string? candidateLabel = request.Label?.Trim();

            if (candidateLabel is not null &&
                candidateLabel.Length > Message.MaximumLocationLabelLength)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            text = null;
            latitude = request.Latitude;
            longitude = request.Longitude;
            label = string.IsNullOrEmpty(candidateLabel)
                ? null
                : candidateLabel;
        }
        else
        {
            // A File message never carries a body (the domain rejects it
            // too) and must name an attachment; an explicit empty guid is a
            // malformed body, not an unknown one. Location payload fields
            // are equally foreign here.
            if (request.Text is not null ||
                request.Latitude is not null ||
                request.Longitude is not null ||
                request.Label is not null ||
                request.AttachmentId is null ||
                request.AttachmentId.Value.Value == Guid.Empty)
            {
                return Result<SendMessageResponse>.Failure(
                    MessageErrors.Invalid);
            }

            text = null;
            latitude = null;
            longitude = null;
            label = null;
        }

        // Idempotency runs BEFORE the attachment is resolved: an exact
        // replay must return the original even though its attachment is
        // already bound by that very message, and must never re-bind it.
        // Sender-bound: another participant cannot claim someone else's
        // message by reusing its key — a key owned by the other sender with
        // any content is the 409, never a 200 of their message.
        Message? existing = await _dbContext.Messages
            .SingleOrDefaultAsync(
                item =>
                    item.ConversationId == conversation.Id &&
                    item.ClientMessageId == clientMessageId,
                cancellationToken);

        if (existing is not null)
        {
            return existing.SenderUserId == request.Requester &&
                IsExactReplay(
                    existing,
                    type,
                    text,
                    request.AttachmentId,
                    latitude,
                    longitude,
                    label)
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

            // Unknown id, another conversation's attachment, another
            // participant's upload, or an EXPIRED unbound upload are
            // indistinguishable 404s — the sender may only ever bind a live
            // attachment of this conversation that they uploaded themselves.
            // Expiry affects unbound uploads only; bound history is never
            // expired away.
            if (attachment is null ||
                attachment.UploadedBy != request.Requester ||
                IsExpiredUnbound(attachment, DateTime.UtcNow, _unboundLifetime))
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
                now,
                latitude,
                longitude,
                label);
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

        // Sending is the only thing that moves conversation activity
        // (monotonic — a delayed retry can never regress it).
        conversation.UpdateLastActivity(now);

        bool durable = _recorder is not NullChatEventRecorder;
        DbContext? context = _dbContext as DbContext;

        if (!durable)
        {
            _dbContext.Messages.Add(message);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception) when (
                exception.Entries.Any(entry => entry.Entity is Attachment))
            {
                // A racing orphan sweep claimed (locked) the attachment first:
                // the conditional UPDATE matched 0 rows and the message insert
                // rolled back with it. No message references a deleted blob.
                return Result<SendMessageResponse>.Failure(
                    AttachmentErrors.NotFound);
            }
            catch (DbUpdateException exception) when (
                ChatPersistenceConflicts.IsUniqueViolation(exception))
            {
                return await ResolveKeyRaceAsync(
                    conversation,
                    request,
                    type,
                    text,
                    latitude,
                    longitude,
                    label,
                    recipient,
                    cancellationToken);
            }

            // Legacy (non-durable) alert path: awaited, not fire-and-forget,
            // failure NOT swallowed — the retry takes the exact replay path.
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

            return Result<SendMessageResponse>.Success(
                new SendMessageResponse(
                    MessageItemFactory.Create(message, readOnUtc: null),
                    Created: true));
        }

        // Durable path: the message, its change-log row, and the
        // notification intent commit atomically. The dispatcher publishes
        // after commit with retries; a crash between commit and publish
        // loses nothing, and a replay never produces a second intent.
        _dbContext.Messages.Add(message);
        _recorder.RecordMessageCreated(conversation, message, recipient, now);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException exception) when (context is not null)
            {
                // A racing orphan sweep claimed (locked) the attachment first,
                // or a concurrent send bound it: the conditional UPDATE matched
                // 0 rows and the message insert rolled back with it. No message
                // references a blob the sweep deleted.
                if (exception.Entries.Any(entry => entry.Entity is Attachment))
                {
                    return Result<SendMessageResponse>.Failure(
                        AttachmentErrors.NotFound);
                }

                if (attempt + 1 < ChatPersistenceConflicts.MaxRevisionRetryAttempts)
                {
                    // A concurrent writer consumed the revision first. Detach
                    // staged rows, reload the authoritative revision, and
                    // re-stage (recorder is duplicate-safe); the message and the
                    // bound attachment stay tracked exactly once.
                    ChatPersistenceConflicts.DetachAdded<ChatChange>(context);
                    ChatPersistenceConflicts.DetachAdded<Domain.Outbox.ChatOutboxItem>(context);
                    await context.Entry(conversation).ReloadAsync(cancellationToken);
                    conversation.UpdateLastActivity(now);
                    _recorder.RecordMessageCreated(conversation, message, recipient, now);
                }
                else
                {
                    throw;
                }
            }
            catch (DbUpdateException exception) when (
                ChatPersistenceConflicts.IsUniqueViolation(exception))
            {
                if (context is not null)
                {
                    context.Entry(message).State = EntityState.Detached;
                    ChatPersistenceConflicts.DetachAdded<ChatChange>(context);
                    ChatPersistenceConflicts.DetachAdded<Domain.Outbox.ChatOutboxItem>(context);
                }

                return await ResolveKeyRaceAsync(
                    conversation,
                    request,
                    type,
                    text,
                    latitude,
                    longitude,
                    label,
                    recipient,
                    cancellationToken);
            }
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
    /// Check-then-insert race: the unique backstop fired, so re-read the
    /// scoped authoritative row. Same key + same sender + same content is
    /// the 200 replay; anything else with the key (including the other
    /// participant's key) is the frozen 409. An attachment-only collision
    /// (no key row) is the single-use 400.
    /// </summary>
    private async Task<Result<SendMessageResponse>> ResolveKeyRaceAsync(
        Conversation conversation,
        SendMessageCommand request,
        MessageType type,
        string? text,
        double? latitude,
        double? longitude,
        string? label,
        UserId recipient,
        CancellationToken cancellationToken)
    {
        string? clientMessageId = request.ClientMessageId?.Trim();

        Message? winner = await _dbContext.Messages
            .SingleOrDefaultAsync(
                item =>
                    item.ConversationId == conversation.Id &&
                    item.ClientMessageId == clientMessageId,
                cancellationToken);

        if (winner is null)
        {
            return Result<SendMessageResponse>.Failure(
                MessageErrors.Invalid);
        }

        return winner.SenderUserId == request.Requester &&
            IsExactReplay(
                winner,
                type,
                text,
                request.AttachmentId,
                latitude,
                longitude,
                label)
            ? Result<SendMessageResponse>.Success(
                new SendMessageResponse(
                    await CreateItemAsync(
                        winner,
                        conversation,
                        request.Requester,
                        recipient,
                        cancellationToken),
                    Created: false))
            : Result<SendMessageResponse>.Failure(
                MessageErrors.IdempotencyConflict);
    }

    internal static bool IsExpiredUnbound(
        Attachment attachment,
        DateTime now,
        TimeSpan? lifetime = null) =>
        !attachment.IsBound &&
        attachment.UploadedOnUtc + (lifetime ?? TimeSpan.FromHours(24)) <= now;

    /// <summary>
    /// Exact replay means the same key with the same content: type, body
    /// (already trimmed, so the comparison is on what was stored), the
    /// location payload (for <c>Location</c> messages) and attachment
    /// reference. Anything else with the same key is the frozen 409
    /// idempotency conflict.
    /// </summary>
    private static bool IsExactReplay(
        Message existing,
        MessageType type,
        string? text,
        AttachmentId? attachmentId,
        double? latitude,
        double? longitude,
        string? label) =>
        existing.Type == type &&
        existing.Text == text &&
        existing.AttachmentId == attachmentId &&
        existing.Latitude == latitude &&
        existing.Longitude == longitude &&
        existing.LocationLabel == label;

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
    /// <c>type</c> is <c>Text</c>, <c>File</c> or <c>Location</c>, matched
    /// by name (not by enum number, so a numeric string is never accepted as
    /// a type).
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

        if (string.Equals(
                trimmed, "Location", StringComparison.OrdinalIgnoreCase))
        {
            type = MessageType.Location;
            return true;
        }

        type = default;
        return false;
    }
}
