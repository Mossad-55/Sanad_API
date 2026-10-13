using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Blocks;

namespace Sanad.Modules.Chat.Application.Blocks;

public sealed class BlockUserCommandHandler
    : ICommandHandler<BlockUserCommand>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatUserLookup _userLookup;
    private readonly IChatAuthorizationChangedNotifier? _notifier;

    public BlockUserCommandHandler(
        IChatDbContext dbContext,
        IChatUserLookup userLookup,
        IChatAuthorizationChangedNotifier? notifier = null)
    {
        _dbContext = dbContext;
        _userLookup = userLookup;
        _notifier = notifier;
    }

    public async Task<Result> Handle(
        BlockUserCommand request,
        CancellationToken cancellationToken)
    {
        // Self-block (and empty ids from a malformed body) is the caller's
        // own invalid request, answered as 400 — unlike unknown foreign ids
        // it reveals nothing the caller does not already know.
        if (request.Blocker == request.Blocked ||
            request.Blocker == UserId.Empty ||
            request.Blocked == UserId.Empty)
        {
            return Result.Failure(BlockErrors.Invalid);
        }

        // The target must be a registered user: blocking a nonexistent id
        // must never create an orphan row (nothing to enforce against, and
        // the row would pollute send-time checks). Unknown ids fail with the
        // privacy 404 so this endpoint cannot be used to probe registrations.
        UserProfile? target = await _userLookup.GetProfileAsync(
            request.Blocked,
            cancellationToken);

        if (target is null)
        {
            return Result.Failure(BlockErrors.NotFound);
        }

        // LANE B SEAM — send-time enforcement (endpoint 4) will read
        // IChatDbContext.Blocks directly; no extra abstraction exists for
        // it. When a blocked user attempts to send, the sender MUST receive
        // the SAME generic failure as a not-participant
        // (Chat.Conversation.NotFound, forced to the generic privacy 404 by
        // the controller) and NEVER a block-specific code — the blocked
        // sender must never learn that a block exists. Unblock (this
        // module's UnblockUserCommandHandler) removes the row, which is the
        // only state Lane B consults, so unblock restores sending. This
        // handler owns only the block state; the send path is Lane B.
        bool alreadyBlocked = await _dbContext.Blocks.AnyAsync(
            block =>
                block.BlockerUserId == request.Blocker &&
                block.BlockedUserId == request.Blocked,
            cancellationToken);

        if (alreadyBlocked)
        {
            // Idempotent 204. The unique index on (BlockerUserId,
            // BlockedUserId) is the database backstop; V1 relies on this
            // pre-check and deliberately does not retry a lost insert race.
            return Result.Success();
        }

        Block block = Block.Create(
            request.Blocker,
            request.Blocked,
            DateTime.UtcNow);

        _dbContext.Blocks.Add(block);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // A committed block suppresses all realtime between the pair from
        // now on; evict their joins/typing before further dispatch.
        if (_notifier is not null)
        {
            await _notifier.PairInvalidatedAsync(
                request.Blocker,
                request.Blocked,
                cancellationToken);
        }

        return Result.Success();
    }
}
