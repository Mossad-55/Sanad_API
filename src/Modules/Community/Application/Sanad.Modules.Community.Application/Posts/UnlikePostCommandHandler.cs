namespace Sanad.Modules.Community.Application.Posts;

using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;

public sealed class UnlikePostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<UnlikePostCommand, bool>
{
    public async Task<Result<bool>> Handle(UnlikePostCommand request, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.FindAsync(
            [new Sanad.BuildingBlocks.Domain.Primitives.Ids.CommunityPostId(request.PostId)],
            cancellationToken);
        if (post is null)
        {
            return false;
        }

        // Undo semantics (testing contract, locked 2026-10-23): reactions
        // stop after deletion, and the deletion contract retains interaction
        // rows (no cleanup exists), so an unlike against a non-published
        // parent is rejected with the same privacy not-found and mutates
        // nothing — the retained like row stays exactly as deletion left it.
        if (post.Status != PostStatus.Published)
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        var interaction = await dbContext.Interactions.SingleOrDefaultAsync(
            i => i.TargetType == CommunityInteractionTarget.Post &&
                 i.TargetId == request.PostId && i.UserId == new UserId(request.UserId) &&
                 i.Kind == CommunityInteractionKind.Like,
            cancellationToken);
        if (interaction is not null)
        {
            dbContext.Interactions.Remove(interaction);
            post.DecrementLikes();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
