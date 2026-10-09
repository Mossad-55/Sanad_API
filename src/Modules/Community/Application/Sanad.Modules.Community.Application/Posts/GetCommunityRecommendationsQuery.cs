using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record GetCommunityRecommendationsQuery(Guid UserId, int Page = 1, int PageSize = 10) : IQuery<IReadOnlyList<Post>>;

public sealed class GetCommunityRecommendationsQueryHandler
    : IQueryHandler<GetCommunityRecommendationsQuery, IReadOnlyList<Post>>
{
    private readonly ICommunityDbContext _dbContext;

    public GetCommunityRecommendationsQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<Post>>> Handle(GetCommunityRecommendationsQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IReadOnlyList<Post> posts = await _dbContext.GetRecommendedPostsAsync(
            new UserId(request.UserId),
            page,
            pageSize,
            cancellationToken);
        return Result<IReadOnlyList<Post>>.Success(posts);
    }
}
