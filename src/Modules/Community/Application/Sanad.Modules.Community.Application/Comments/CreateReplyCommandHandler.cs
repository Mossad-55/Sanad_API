namespace Sanad.Modules.Community.Application.Comments;

using Sanad.BuildingBlocks.Domain.Primitives.Ids;

public sealed class CreateReplyCommandHandler(ICommunityDbContext dbContext)
    : ICommandHandler<CreateReplyCommand, Reply>
{
    public async Task<Result<Reply>> Handle(CreateReplyCommand request, CancellationToken cancellationToken)
    {
        var commentExists = await dbContext.Comments.AnyAsync(
            comment => comment.Id == new CommunityCommentId(request.CommentId), cancellationToken);
        if (!commentExists || request.AuthorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ContentArabic) ||
            string.IsNullOrWhiteSpace(request.ContentEnglish) ||
            request.ContentArabic.Trim().Length > 500 || request.ContentEnglish.Trim().Length > 500)
        {
            throw new Sanad.BuildingBlocks.Domain.Exceptions.DomainException("Reply is invalid or its comment does not exist.");
        }

        var reply = Reply.Create(new CommunityCommentId(request.CommentId), new UserId(request.AuthorId),
            request.ContentArabic, request.ContentEnglish, DateTime.UtcNow);

        dbContext.Replies.Add(reply);
        await dbContext.SaveChangesAsync(cancellationToken);
        return reply;
    }
}
