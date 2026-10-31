using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Domain.Interactions;

namespace Ahlena.Modules.Community.Application.Posts;

public sealed class LikePostCommandHandler : ICommandHandler<LikePostCommand, bool>
{
    private readonly ICommunityDbContext _dbContext;

    public LikePostCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(LikePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _dbContext.Posts.FindAsync(
            [new Ahlena.BuildingBlocks.Domain.Primitives.Ids.CommunityPostId(request.PostId)],
            cancellationToken);

        if (post is null || request.UserId == Guid.Empty)
        {
            return false;
        }

        // Reactions stop after deletion (testing contract, locked 2026-10-23):
        // a like only ever starts or continues against a still-published
        // parent. A deleted post answers the same privacy not-found as a
        // missing one and nothing is persisted.
        if (post.Status != PostStatus.Published)
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        var alreadyLiked = await _dbContext.Interactions.AnyAsync(
            i => i.TargetType == CommunityInteractionTarget.Post &&
                 i.TargetId == request.PostId && i.UserId == new UserId(request.UserId) &&
                 i.Kind == CommunityInteractionKind.Like,
            cancellationToken);
        if (!alreadyLiked)
        {
            _dbContext.Interactions.Add(CommunityInteraction.Create(
                CommunityInteractionTarget.Post, request.PostId, new UserId(request.UserId),
                CommunityInteractionKind.Like, DateTime.UtcNow));
            post.IncrementLikes();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return true;
    }
}
