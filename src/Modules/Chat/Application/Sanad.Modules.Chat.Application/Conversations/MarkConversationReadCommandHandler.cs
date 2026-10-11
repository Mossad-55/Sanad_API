using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Conversations;

public sealed class MarkConversationReadCommandHandler
    : ICommandHandler<MarkConversationReadCommand>
{
    private readonly IChatDbContext _dbContext;

    public MarkConversationReadCommandHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
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
        // understand. See ConversationReadErrors for why a *wrong but
        // non-empty* message id is never rejected here.
        if (request.LastReadMessageId == Guid.Empty)
        {
            return Result.Failure(ConversationReadErrors.Invalid);
        }

        DateTime now = DateTime.UtcNow;

        // Upsert: one row per (conversation, user). InMemory tests prove the
        // handler-level pre-check; the unique index on (ConversationId,
        // UserId) is the database backstop. V1 deliberately does not retry a
        // lost insert race.
        ConversationReadState? state = await _dbContext.ReadStates
            .SingleOrDefaultAsync(
                item =>
                    item.ConversationId == request.ConversationId &&
                    item.UserId == request.Requester,
                cancellationToken);

        if (state is null)
        {
            _dbContext.ReadStates.Add(ConversationReadState.Create(
                request.ConversationId,
                request.Requester,
                request.LastReadMessageId,
                now));
        }
        else
        {
            // Idempotent re-mark: same row, refreshed mark and stamp —
            // never a duplicate row.
            state.Mark(request.LastReadMessageId, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // LANE B SEAM — Lane A records the mark only. It cannot validate
        // LastReadMessageId against a message (there is no Message table in
        // Lane A) and deliberately does not: unread counts, read ticks, and
        // any ordering rule are Lane B's to interpret from ReadStates.
        // A read mark is also NOT activity: Conversation.LastActivityOnUtc
        // is left untouched (it belongs to the message flow), and hiding /
        // unhiding / reopening never creates or clears a mark.
        return Result.Success();
    }
}
