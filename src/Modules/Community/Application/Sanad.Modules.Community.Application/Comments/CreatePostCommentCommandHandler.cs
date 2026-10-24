using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Comments;

public sealed class CreatePostCommentCommandHandler : ICommandHandler<CreatePostCommentCommand, Comment>
{
    private readonly ICommunityDbContext _dbContext;

    public CreatePostCommentCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Comment>> Handle(CreatePostCommentCommand request, CancellationToken cancellationToken)
    {
        var authorId = new UserId(request.AuthorId);
        if (await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
                _dbContext, authorId, DateTime.UtcNow, cancellationToken))
        {
            return Result<Comment>.Failure(CommunityModerationErrors.AuthorRestricted);
        }

        // A deleted post is gone for its author and its readers alike: new
        // comments cannot continue against it. The privacy not-found shape
        // reveals nothing about the removed parent.
        bool parentDeleted = await _dbContext.Posts.AsNoTracking()
            .AnyAsync(
                post => post.Id == new CommunityPostId(request.PostId) &&
                        post.Status == PostStatus.Deleted,
                cancellationToken);
        if (parentDeleted)
        {
            return Result<Comment>.Failure(CommunityPostLifecycleErrors.PostNotFound);
        }

        var comment = Comment.Create(new CommunityPostId(request.PostId), authorId,
            request.ContentArabic, request.ContentEnglish, DateTime.UtcNow);

        _dbContext.Comments.Add(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return comment;
    }
}
