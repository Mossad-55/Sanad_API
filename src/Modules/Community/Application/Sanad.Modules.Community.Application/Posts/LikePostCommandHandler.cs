using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.Modules.Community.Application.Posts;

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
            [new Sanad.BuildingBlocks.Domain.Primitives.Ids.CommunityPostId(request.PostId)],
            cancellationToken);

        if (post is null || request.UserId == Guid.Empty)
        {
            return false;
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
