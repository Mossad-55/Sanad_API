using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Comments;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Application.Comments;

public sealed class CreateCommentCommandHandler : ICommandHandler<CreateCommentCommand, Comment>
{
    private readonly ICommunityDbContext _dbContext;

    public CreateCommentCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Comment>> Handle(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        var authorId = new UserId(request.AuthorId);
        if (await CommunityAuthorRestrictionGuard.IsAuthorRestrictedAsync(
                _dbContext, authorId, DateTime.UtcNow, cancellationToken))
        {
            return Result<Comment>.Failure(CommunityModerationErrors.AuthorRestricted);
        }

        var comment = Comment.Create(new CommunityPostId(request.PostId), authorId,
            request.ContentArabic, request.ContentEnglish, DateTime.UtcNow);

        _dbContext.Comments.Add(comment);

        var post = await _dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);
        if (post is not null)
        {
            post.IncrementComments();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return comment;
    }
}
