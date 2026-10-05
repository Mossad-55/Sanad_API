using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.Modules.Community.Application.Comments;

public sealed record LikeCommunityCommentCommand(Guid CommentId, Guid UserId) : ICommand<bool>;

public sealed class LikeCommunityCommentCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<LikeCommunityCommentCommand, bool>
{
    public async Task<Result<bool>> Handle(LikeCommunityCommentCommand request, CancellationToken cancellationToken)
    {
        var comment = await dbContext.Comments.FindAsync(
            [new Sanad.BuildingBlocks.Domain.Primitives.Ids.CommunityCommentId(request.CommentId)],
            cancellationToken);
        if (comment is null || request.UserId == Guid.Empty) return false;

        var userId = new Sanad.BuildingBlocks.Domain.Primitives.Ids.UserId(request.UserId);
        var interaction = await dbContext.Interactions.SingleOrDefaultAsync(
            item => item.TargetType == CommunityInteractionTarget.Comment &&
                    item.TargetId == request.CommentId && item.UserId == userId &&
                    item.Kind == CommunityInteractionKind.Like,
            cancellationToken);

        if (interaction is null)
        {
            dbContext.Interactions.Add(CommunityInteraction.Create(
                CommunityInteractionTarget.Comment,
                request.CommentId,
                userId,
                CommunityInteractionKind.Like,
                DateTime.UtcNow));
            comment.IncrementLikes();
        }
        else
        {
            dbContext.Interactions.Remove(interaction);
            comment.DecrementLikes();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
