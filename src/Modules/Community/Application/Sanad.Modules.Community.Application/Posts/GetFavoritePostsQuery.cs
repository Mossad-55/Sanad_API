using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record GetFavoritePostsQuery(Guid UserId, int Page = 1, int PageSize = 20)
    : IQuery<IReadOnlyList<PostFeedItem>>;

public sealed class GetFavoritePostsQueryHandler
    : IQueryHandler<GetFavoritePostsQuery, IReadOnlyList<PostFeedItem>>
{
    private readonly ICommunityDbContext _dbContext;

    public GetFavoritePostsQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<PostFeedItem>>> Handle(
        GetFavoritePostsQuery request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
            return Result<IReadOnlyList<PostFeedItem>>.Success([]);

        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        var user = new UserId(request.UserId);

        List<Guid> favoritedPostIds = await _dbContext.Interactions
            .AsNoTracking()
            .Where(i => i.TargetType == CommunityInteractionTarget.Post &&
                        i.Kind == CommunityInteractionKind.Favorite &&
                        i.UserId == user)
            .OrderByDescending(i => i.CreatedOnUtc)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => i.TargetId)
            .ToListAsync(cancellationToken);

        if (favoritedPostIds.Count == 0)
            return Result<IReadOnlyList<PostFeedItem>>.Success([]);

        List<CommunityPostId> favoritedPosts = favoritedPostIds.Select(id => new CommunityPostId(id)).ToList();
        IReadOnlyList<Post> posts = await _dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.Status == PostStatus.Published && favoritedPosts.Contains(p.Id))
            .ToListAsync(cancellationToken);

        // Preserve newest-favorited-first order from the interaction page.
        var postsById = posts.ToDictionary(p => p.Id.Value);
        List<Post> ordered = favoritedPostIds
            .Where(postsById.ContainsKey)
            .Select(id => postsById[id])
            .ToList();

        var state = await PostViewerState.LoadAsync(
            _dbContext, request.UserId, ordered, cancellationToken);

        return Result<IReadOnlyList<PostFeedItem>>.Success(PostViewerState.Attach(ordered, state));
    }
}
