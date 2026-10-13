using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Events;
using Sanad.Modules.Chat.Application.Messages;
using Sanad.Modules.Chat.Application.Persistence;
using Sanad.Modules.Chat.Domain.Changes;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class MarkConversationReadCommandHandler
    : ICommandHandler<MarkConversationReadCommand>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatEventRecorder _recorder;

    public MarkConversationReadCommandHandler(
        IChatDbContext dbContext,
        IChatEventRecorder? recorder = null)
    {
        _dbContext = dbContext;
        _recorder = recorder ?? NullChatEventRecorder.Instance;
    }

    public async Task<Result> Handle(
        MarkConversationReadCommand request,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        // The privacy gate runs FIRST and answers identically for unknown
        // and foreign conversations, so a non-participant can never tell
        // whether a conversation id exists — not even by sending a
        // malformed body (that still ends here as a 404, never a 400).
        if (conversation is null ||
            !conversation.IsParticipant(request.Requester))
        {
            return Result.Failure(ConversationErrors.NotFound);
        }

        // Only a participant reaches this, and the mistake is their own:
        // an empty/missing message reference is a 400 they are entitled to
        // understand.
        if (request.LastReadMessageId == Guid.Empty)
        {
            return Result.Failure(ConversationReadErrors.Invalid);
        }

        // The read target must exist in THIS conversation; an unknown or
        // foreign message id is the privacy 404, so the mark can never be
        // used to probe message ids.
        Message? target = await _dbContext.Messages
            .SingleOrDefaultAsync(
                item =>
                    item.Id == new MessageId(request.LastReadMessageId) &&
                    item.ConversationId == request.ConversationId,
                cancellationToken);

        if (target is null)
        {
            return Result.Failure(ConversationErrors.NotFound);
        }

        DateTime now = DateTime.UtcNow;
        DbContext? context = _dbContext as DbContext;

        for (int attempt = 0; ; attempt++)
        {
            ConversationReadState? state = await _dbContext.ReadStates
                .SingleOrDefaultAsync(
                    item =>
                        item.ConversationId == request.ConversationId &&
                        item.UserId == request.Requester,
                    cancellationToken);

            if (state is null)
            {
                state = ConversationReadState.Create(
                    request.ConversationId,
                    request.Requester,
                    request.LastReadMessageId,
                    now);
                _dbContext.ReadStates.Add(state);
            }
            else
            {
                bool advances = await AdvancesAsync(
                    state,
                    target,
                    cancellationToken);

                if (!advances)
                {
                    // Monotonic watermarks: an older or equal mark succeeds
                    // without regression and without touching first-read
                    // stamps. Idempotent re-mark, same row, no write at all.
                    return Result.Success();
                }

                state.Mark(request.LastReadMessageId, now);
            }

            // Stable first-read stamps: covered incoming messages without
            // one gain their first (and final) read stamp now; already
            // stamped messages are never regenerated from a newer mark.
            List<Message> covered = await LoadCoveredIncomingAsync(
                conversation,
                request.Requester,
                target,
                cancellationToken);

            foreach (Message message in covered)
            {
                if (message.ReadOnUtc is null)
                {
                    message.MarkRead(now);
                    _recorder.RecordReceiptUpdated(conversation, message, now);
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException) when (
                context is not null &&
                attempt + 1 < ChatPersistenceConflicts.MaxRevisionRetryAttempts)
            {
                // Lost the revision race (or the read-state insert race at
                // the token level): detach staged rows, reload, and
                // re-evaluate — the monotonic check above makes the retry
                // converge instead of regressing.
                ChatPersistenceConflicts.DetachAdded<ChatChange>(context);
                ChatPersistenceConflicts.DetachAdded<ConversationReadState>(context);
                await context.Entry(conversation).ReloadAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (
                ChatPersistenceConflicts.IsUniqueViolation(exception))
            {
                // A concurrent mark inserted the read-state row first:
                // detach ours and re-evaluate monotonically.
                if (context is not null)
                {
                    ChatPersistenceConflicts.DetachAdded<ConversationReadState>(context);
                    ChatPersistenceConflicts.DetachAdded<ChatChange>(context);
                    await context.Entry(conversation).ReloadAsync(cancellationToken);
                }

                if (attempt + 1 >= ChatPersistenceConflicts.MaxRevisionRetryAttempts)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// A mark advances only when its target sorts strictly after the current
    /// watermark in server total order (sent, id). An equal mark or an older
    /// one is a successful no-op. A legacy mark whose id resolves nowhere in
    /// this conversation always advances (the timestamp fallback already
    /// treats it as covering everything up to its own stamp).
    /// </summary>
    private async Task<bool> AdvancesAsync(
        ConversationReadState state,
        Message target,
        CancellationToken cancellationToken)
    {
        if (state.LastReadMessageId == target.Id.Value)
        {
            return false;
        }

        Message? current = await _dbContext.Messages
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == new MessageId(state.LastReadMessageId) &&
                    item.ConversationId == state.ConversationId,
                cancellationToken);

        if (current is null)
        {
            return true;
        }

        int byTime = target.SentOnUtc.CompareTo(current.SentOnUtc);

        return byTime > 0 ||
            (byTime == 0 &&
             target.Id.Value.CompareTo(current.Id.Value) > 0);
    }

    private async Task<List<Message>> LoadCoveredIncomingAsync(
        Conversation conversation,
        UserId requester,
        Message target,
        CancellationToken cancellationToken)
    {
        // Covered = incoming messages at/below the watermark in total order.
        // The id tie-break is in memory (Guid comparison is Npgsql-safe
        // only client-side); timestamps are server-set so ties are rare.
        List<Message> candidates = await _dbContext.Messages
            .Where(item =>
                item.ConversationId == conversation.Id &&
                item.SenderUserId != requester &&
                item.SentOnUtc <= target.SentOnUtc)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(item =>
                item.SentOnUtc < target.SentOnUtc ||
                (item.SentOnUtc == target.SentOnUtc &&
                 item.Id.Value.CompareTo(target.Id.Value) <= 0))
            .ToList();
    }
}
