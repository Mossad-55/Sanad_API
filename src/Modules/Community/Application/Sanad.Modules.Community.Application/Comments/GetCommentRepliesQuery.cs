namespace Sanad.Modules.Community.Application.Comments;

using Sanad.BuildingBlocks.Domain.Primitives.Ids;

public sealed record GetCommentRepliesQuery(Guid CommentId, int Page = 1, int PageSize = 20) : IQuery<Reply[]>;

public sealed class GetCommentRepliesQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetCommentRepliesQuery, Reply[]>
{
    public async Task<Result<Reply[]>> Handle(GetCommentRepliesQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);
        Reply[] replies = await dbContext.Replies.AsNoTracking()
            .Where(reply => reply.CommentId == new CommunityCommentId(request.CommentId))
            .OrderBy(reply => reply.CreatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return Result<Reply[]>.Success(replies);
    }
}
