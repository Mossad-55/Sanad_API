namespace Sanad.Modules.Community.Application.Blocks;

using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Blocks;

/// <summary>
/// Failure codes of the Community block endpoints.
/// <see cref="Invalid"/> answers the viewer's own mistake (a self-block) as a
/// 400 — it reveals nothing the caller does not already know.
/// <see cref="NotFound"/> (an unknown target author) is forced by the
/// Community controller into the same generic 404 the rest of the Community
/// reads use. The body is generic, but the route still separates a known
/// target (204) from an unknown one (404), exactly like the accepted Chat
/// block design. Unblock deliberately has no codes: a missing row succeeds
/// (204 idempotent) and never reveals whether a block existed.
/// </summary>
public static class CommunityBlockErrors
{
    public static readonly Error Invalid =
        new("Community.Block.Invalid", "Users cannot block themselves.");

    public static readonly Error NotFound =
        new("Community.Block.NotFound", "Blocked author not found.");
}

/// <summary>
/// The viewer blocks an author. Idempotent: blocking an already-blocked author
/// succeeds again (204) without creating a duplicate row.
/// </summary>
public sealed record BlockCommunityAuthorCommand(
    UserId Blocker,
    UserId BlockedAuthor)
    : ICommand;

public sealed class BlockCommunityAuthorCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<BlockCommunityAuthorCommand>
{
    public async Task<Result> Handle(
        BlockCommunityAuthorCommand request,
        CancellationToken cancellationToken)
    {
        // Self-block (and empty ids from a malformed body) is the caller's own
        // invalid request, answered as 400 — unlike an unknown target it
        // reveals nothing the caller does not already know.
        if (request.Blocker == request.BlockedAuthor ||
            request.Blocker == UserId.Empty ||
            request.BlockedAuthor == UserId.Empty)
        {
            return Result.Failure(CommunityBlockErrors.Invalid);
        }

        // The target must be a registered author: blocking an unknown id must
        // never create an orphan row. Unknown ids fail with the generic 404
        // body; a known target answers 204 and an unknown one 404, so the
        // route still distinguishes a registered account id from an
        // unregistered one — the accepted Chat block design.
        IReadOnlyDictionary<Guid, CommunityAuthorName> authors =
            await dbContext.GetCommunityAuthorNamesAsync(
                [request.BlockedAuthor.Value],
                cancellationToken);

        if (!authors.ContainsKey(request.BlockedAuthor.Value))
        {
            return Result.Failure(CommunityBlockErrors.NotFound);
        }

        // Idempotent 204. The unique index on (blocker, blocked author) is the
        // database backstop.
        bool alreadyBlocked = await dbContext.CommunityAuthorBlocks.AnyAsync(
            block =>
                block.BlockerUserId == request.Blocker &&
                block.BlockedAuthorUserId == request.BlockedAuthor,
            cancellationToken);

        if (alreadyBlocked)
        {
            return Result.Success();
        }

        dbContext.CommunityAuthorBlocks.Add(CommunityAuthorBlock.Create(
            request.Blocker,
            request.BlockedAuthor,
            DateTime.UtcNow));

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// The viewer removes the block over an author. Idempotent: a missing row
/// still succeeds (204).
/// </summary>
public sealed record UnblockCommunityAuthorCommand(
    UserId Blocker,
    UserId BlockedAuthor)
    : ICommand;

public sealed class UnblockCommunityAuthorCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<UnblockCommunityAuthorCommand>
{
    public async Task<Result> Handle(
        UnblockCommunityAuthorCommand request,
        CancellationToken cancellationToken)
    {
        CommunityAuthorBlock? block = await dbContext.CommunityAuthorBlocks
            .SingleOrDefaultAsync(
                item =>
                    item.BlockerUserId == request.Blocker &&
                    item.BlockedAuthorUserId == request.BlockedAuthor,
                cancellationToken);

        // A missing row still succeeds (204 idempotent). Unblock is the
        // viewer's own action, so always-204 reveals nothing to a third party
        // and keeps client retries safe. Removing the row is the complete
        // enforcement off-switch: the post reads consult the relation
        // directly, so no row means the author's posts are visible again.
        if (block is not null)
        {
            dbContext.CommunityAuthorBlocks.Remove(block);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
