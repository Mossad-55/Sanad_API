using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Comments;

public sealed record GetPostCommentsQuery(Guid PostId, int Page, int PageSize) : IQuery<CommentPage>;

public sealed record CommentPage
{
    public IReadOnlyList<Comment> Items { get; set; } = new List<Comment>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public sealed class GetPostCommentsQueryHandler
    : IQueryHandler<GetPostCommentsQuery, CommentPage>
{
    private readonly ICommunityDbContext _dbContext;

    public GetPostCommentsQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CommentPage>> Handle(GetPostCommentsQuery request, CancellationToken cancellationToken)
    {
        var totalCount = await _dbContext.Comments
            .Where(c => c.PostId == new CommunityPostId(request.PostId))
            .CountAsync(cancellationToken);

        var comments = await _dbContext.Comments
            .Where(c => c.PostId == new CommunityPostId(request.PostId))
            .OrderByDescending(c => c.CreatedOnUtc)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return Result<CommentPage>.Success(new CommentPage
        {
            Items = comments,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }
}
