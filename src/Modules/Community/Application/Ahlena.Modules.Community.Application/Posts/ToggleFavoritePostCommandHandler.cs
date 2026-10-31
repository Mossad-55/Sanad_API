using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Domain.Interactions;

namespace Ahlena.Modules.Community.Application.Posts;

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
            [new Ahlena.BuildingBlocks.Domain.Primitives.Ids.CommunityPostId(request.PostId)],
            cancellationToken);

        if (post is null || request.UserId == Guid.Empty)
        {
            return false;
        }

        // Favorites stop after deletion (testing contract, locked
        // 2026-10-23): neither starting nor un-favoriting continues against
        // a deleted (or any non-published) parent; the retained favorite row
        // stays as deletion left it.
        if (post.Status != PostStatus.Published)
        {
            return Result<bool>.Failure(CommunityPostLifecycleErrors.PostNotFound);
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
