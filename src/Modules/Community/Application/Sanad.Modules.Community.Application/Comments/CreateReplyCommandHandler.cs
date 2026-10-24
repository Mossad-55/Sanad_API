namespace Sanad.Modules.Community.Application.Comments;

using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Posts;

public sealed class CreateReplyCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<CreateReplyCommand, Reply>
{
    public async Task<Result<Reply>> Handle(CreateReplyCommand request, CancellationToken cancellationToken)
    {
        var authorId = new UserId(request.AuthorId);
        if (await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
                dbContext, authorId, DateTime.UtcNow, cancellationToken))
        {
            return Result<Reply>.Failure(CommunityModerationErrors.AuthorRestricted);
        }

        // Replies cannot continue against a comment whose parent post was
        // deleted: the thread is gone with the post.
        bool parentDeleted = await dbContext.Comments.AsNoTracking()
            .AnyAsync(
                comment => comment.Id == new CommunityCommentId(request.CommentId) &&
                           comment.Post.Status == PostStatus.Deleted,
                cancellationToken);
        if (parentDeleted)
        {
            return Result<Reply>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        var commentExists = await dbContext.Comments.AnyAsync(
            comment => comment.Id == new CommunityCommentId(request.CommentId), cancellationToken);
        if (!commentExists || request.AuthorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ContentArabic) ||
            string.IsNullOrWhiteSpace(request.ContentEnglish) ||
            request.ContentArabic.Trim().Length > 500 || request.ContentEnglish.Trim().Length > 500)
        {
            throw new Sanad.BuildingBlocks.Domain.Exceptions.DomainException("Reply is invalid or its comment does not exist.");
        }

        var reply = Reply.Create(new CommunityCommentId(request.CommentId), authorId,
            request.ContentArabic, request.ContentEnglish, DateTime.UtcNow);

        dbContext.Replies.Add(reply);
        await dbContext.SaveChangesAsync(cancellationToken);
        return reply;
    }
}
