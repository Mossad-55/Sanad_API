using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record GetPostsQuery(PostStatus Status, int Page = 1, int PageSize = 20) : IQuery<IReadOnlyList<Post>>;

public sealed class GetPostsQueryHandler
    : IQueryHandler<GetPostsQuery, IReadOnlyList<Post>>
{
    private readonly ICommunityDbContext _dbContext;

    public GetPostsQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<Post>>> Handle(GetPostsQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IReadOnlyList<Post> posts = await _dbContext.Posts
            .AsNoTracking()
            .Where(p => p.Status == request.Status)
            .OrderByDescending(p => p.CreatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<Post>>.Success(posts);
    }
}
