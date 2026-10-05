using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.BuildingBlocks.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Comments;

public sealed class UpdateCommentCommandHandler : ICommandHandler<UpdateCommentCommand, Comment?>
{
    private readonly ICommunityDbContext _dbContext;

    public UpdateCommentCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Comment?>> Handle(UpdateCommentCommand request, CancellationToken cancellationToken)
    {
        var comment = await _dbContext.Comments
            .SingleOrDefaultAsync(x => x.Id == new CommunityCommentId(request.CommentId), cancellationToken);

        if (comment is null)
        {
            return Result<Comment?>.Success(null);
        }

        if (!comment.IsOwnedBy(new UserId(request.AuthorId)) ||
            string.IsNullOrWhiteSpace(request.ContentArabic) ||
            string.IsNullOrWhiteSpace(request.ContentEnglish) ||
            request.ContentArabic.Trim().Length > 500 ||
            request.ContentEnglish.Trim().Length > 500)
        {
            return Result<Comment?>.Success(null);
        }

        comment.UpdateContent(request.ContentArabic, request.ContentEnglish, DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return comment;
    }
}
