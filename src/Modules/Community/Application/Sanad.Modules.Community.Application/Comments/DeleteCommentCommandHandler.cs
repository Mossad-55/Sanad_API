using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Comments;

public sealed class DeleteCommentCommandHandler : ICommandHandler<DeleteCommentCommand, CommentDeleteResult>
{
    private readonly ICommunityDbContext _dbContext;

    public DeleteCommentCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CommentDeleteResult>> Handle(DeleteCommentCommand request, CancellationToken cancellationToken)
    {
        var comment = await _dbContext.Comments.FindAsync([new CommunityCommentId(request.CommentId)], cancellationToken);

        if (comment is null)
        {
            return new CommentDeleteResult { Success = false, Error = "NotFound" };
        }

        if (comment.AuthorId != new UserId(request.AuthorId))
            return new CommentDeleteResult { Success = false, Error = "Forbidden" };

        // Check if comment has replies (safeguard)
        var hasReplies = await _dbContext.Replies
            .AnyAsync(r => r.CommentId == new CommunityCommentId(request.CommentId), cancellationToken);
        if (hasReplies)
        {
            return new CommentDeleteResult
            {
                Success = false,
                Error = "CommentHasReplies"
            };
        }

        var post = await _dbContext.Posts.FindAsync([comment.PostId], cancellationToken);
        if (post is not null)
            post.DecrementComments();

        _dbContext.Comments.Remove(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CommentDeleteResult { Success = true };
    }
}

public sealed record CommentDeleteResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
}
