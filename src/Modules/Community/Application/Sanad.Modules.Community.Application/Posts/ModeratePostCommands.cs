using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record PublishCommunityPostCommand(Guid PostId, UserId ModeratorId) : ICommand<bool>;
public sealed record RejectCommunityPostCommand(Guid PostId, UserId ModeratorId) : ICommand<bool>;

public sealed class PublishCommunityPostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<PublishCommunityPostCommand, bool>
{
    public async Task<Result<bool>> Handle(PublishCommunityPostCommand request, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);
        if (post is null)
            return Result<bool>.Failure(new Error("Community.PostNotFound", "Community post not found."));

        try { post.Publish(request.ModeratorId, DateTime.UtcNow); }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<bool>.Failure(new Error("Community.InvalidModerationOperation", exception.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}

public sealed class RejectCommunityPostCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<RejectCommunityPostCommand, bool>
{
    public async Task<Result<bool>> Handle(RejectCommunityPostCommand request, CancellationToken cancellationToken)
    {
        var post = await dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);
        if (post is null)
            return Result<bool>.Failure(new Error("Community.PostNotFound", "Community post not found."));

        try { post.Reject(DateTime.UtcNow); }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<bool>.Failure(new Error("Community.InvalidModerationOperation", exception.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
