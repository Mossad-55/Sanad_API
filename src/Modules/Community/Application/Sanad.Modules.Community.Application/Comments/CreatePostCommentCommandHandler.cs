using Sanad.Modules.Community.Application.Abstractions.Data;
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
        var comment = Comment.Create(new CommunityPostId(request.PostId), new UserId(request.AuthorId),
            request.ContentArabic, request.ContentEnglish, DateTime.UtcNow);

        _dbContext.Comments.Add(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return comment;
    }
}
