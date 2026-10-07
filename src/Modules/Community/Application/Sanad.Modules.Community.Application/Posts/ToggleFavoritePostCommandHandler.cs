using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.Modules.Community.Application.Posts;

public sealed class ToggleFavoritePostCommandHandler : ICommandHandler<ToggleFavoritePostCommand, bool>
{
    private readonly ICommunityDbContext _dbContext;

    public ToggleFavoritePostCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(ToggleFavoritePostCommand request, CancellationToken cancellationToken)
    {
        var post = await _dbContext.Posts.FindAsync(
            [new Sanad.BuildingBlocks.Domain.Primitives.Ids.CommunityPostId(request.PostId)],
            cancellationToken);

        if (post is null || request.UserId == Guid.Empty)
        {
            return false;
        }

        var interaction = await _dbContext.Interactions.SingleOrDefaultAsync(
            i => i.TargetType == CommunityInteractionTarget.Post && i.TargetId == request.PostId &&
                 i.UserId == new UserId(request.UserId) && i.Kind == CommunityInteractionKind.Favorite,
            cancellationToken);
        if (interaction is null)
        {
            _dbContext.Interactions.Add(CommunityInteraction.Create(
                CommunityInteractionTarget.Post, request.PostId, new UserId(request.UserId),
                CommunityInteractionKind.Favorite, DateTime.UtcNow));
            post.IncrementFavorites();
        }
        else
        {
            _dbContext.Interactions.Remove(interaction);
            post.DecrementFavorites();
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
