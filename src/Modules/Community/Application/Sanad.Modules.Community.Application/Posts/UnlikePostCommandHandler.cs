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
