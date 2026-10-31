using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Domain.Interactions;
using Ahlena.Modules.Community.Domain.Posts;

namespace Ahlena.Modules.Community.Application.Posts;

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

        // The posts of the authors the viewer blocked. The relation is
        // resolved with one constant query — never one per row — and the ids
        // are compared against the interaction's target id, so the filter is
        // composed inside the interaction query, before the ordering and the
        // paging, and a page never shrinks silently. Accepted tech debt: the
        // id array grows with the number of published posts the blocked
        // authors have.
        Guid[] blockedPostIds = await LoadBlockedPostIdsAsync(
            _dbContext,
            user,
            cancellationToken);

        IQueryable<CommunityInteraction> interactions = _dbContext.Interactions
            .AsNoTracking()
            .Where(i => i.TargetType == CommunityInteractionTarget.Post &&
                        i.Kind == CommunityInteractionKind.Favorite &&
                        i.UserId == user);

        if (blockedPostIds.Length > 0)
        {
            interactions = interactions.Where(i => !blockedPostIds.Contains(i.TargetId));
        }

        List<Guid> favoritedPostIds = await interactions
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

    /// <summary>
    /// The ids of the published posts written by the authors the viewer
    /// blocked. One query for the whole page; the result is empty when the
    /// viewer blocked nobody.
    /// </summary>
    private static async Task<Guid[]> LoadBlockedPostIdsAsync(
        ICommunityDbContext dbContext,
        UserId viewerUserId,
        CancellationToken cancellationToken)
    {
        if (viewerUserId == UserId.Empty)
        {
            return [];
        }

        List<CommunityPostId> blockedPostIds = await dbContext.Posts
            .AsNoTracking()
            .Where(post =>
                post.Status == PostStatus.Published &&
                dbContext.CommunityAuthorBlocks.Any(
                    block =>
                        block.BlockerUserId == viewerUserId &&
                        block.BlockedAuthorUserId == post.AuthorId))
            .Select(post => post.Id)
            .ToListAsync(cancellationToken);

        return blockedPostIds.Select(id => id.Value).ToArray();
    }
}
