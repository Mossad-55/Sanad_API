using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Categories;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record GetPostsQuery(
    PostStatus Status,
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    Guid? CategoryId = null,
    DateTime? FromDateUtc = null,
    DateTime? ToDateUtc = null,
    CommunityPostsSort Sort = CommunityPostsSort.Newest,
    Guid? ViewerUserId = null) : IQuery<IReadOnlyList<PostFeedItem>>;

public sealed class GetPostsQueryHandler
    : IQueryHandler<GetPostsQuery, IReadOnlyList<PostFeedItem>>
{
    private readonly ICommunityDbContext _dbContext;

    public GetPostsQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<PostFeedItem>>> Handle(GetPostsQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IQueryable<Post> query = _dbContext.Posts
            .AsNoTracking()
            .Where(p => p.Status == request.Status);

        string? term = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToLower();
        if (term is not null)
        {
            query = query.Where(p =>
                p.TitleArabic.ToLower().Contains(term) ||
                p.TitleEnglish.ToLower().Contains(term) ||
                p.ContentArabic.ToLower().Contains(term) ||
                p.ContentEnglish.ToLower().Contains(term));
        }

        if (request.CategoryId is Guid categoryId)
        {
            var id = new CommunityCategoryId(categoryId);
            query = query.Where(p => p.CategoryId == id);
        }

        if (request.FromDateUtc is DateTime from)
        {
            query = query.Where(p => p.CreatedOnUtc >= from);
        }

        if (request.ToDateUtc is DateTime to)
        {
            query = query.Where(p => p.CreatedOnUtc <= to);
        }

        // The viewer's blocked authors are excluded from the feed. The
        // predicate is composed inside the query, before the ordering and the
        // paging, so a page never shrinks silently.
        if (request.ViewerUserId is Guid viewerId && viewerId != Guid.Empty)
        {
            UserId viewerUserId = new(viewerId);
            query = query.Where(p => !_dbContext.CommunityAuthorBlocks.Any(
                block =>
                    block.BlockerUserId == viewerUserId &&
                    block.BlockedAuthorUserId == p.AuthorId));
        }

        query = request.Sort switch
        {
            CommunityPostsSort.Oldest =>
                query.OrderBy(p => p.CreatedOnUtc).ThenBy(p => p.Id),
            CommunityPostsSort.MostLiked =>
                query.OrderByDescending(p => p.LikesCount)
                    .ThenByDescending(p => p.CreatedOnUtc)
                    .ThenByDescending(p => p.Id),
            _ =>
                query.OrderByDescending(p => p.CreatedOnUtc).ThenByDescending(p => p.Id)
        };

        IReadOnlyList<Post> posts = await query
            .Include(p => p.Category)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var state = await PostViewerState.LoadAsync(
            _dbContext, request.ViewerUserId ?? Guid.Empty, posts, cancellationToken);

        return Result<IReadOnlyList<PostFeedItem>>.Success(PostViewerState.Attach(posts, state));
    }
}

/// <summary>
/// The display projection of one Community author, resolved from the shared
/// Identity database.
/// <para>
/// <c>DisplayName</c> is the English full name preferred with the Arabic full
/// name as fallback (the same rule the Identity inbox and the Chat participant
/// use). It is <c>null</c> when the identity row is missing or when both names
/// are blank — no placeholder name is invented.
/// </para>
/// <para>
/// <c>AvatarUrl</c> is always <c>null</c>: Community has no publicly servable
/// avatar source. The only stored avatar reference is the Identity file key,
/// which lives in private storage and is readable only by its owner through
/// <c>GET /api/v1/auth/avatar</c>; publishing that key as if it were a URL
/// would hand the app a link that always 404s. The honest value is null.
/// </para>
/// </summary>
public sealed record CommunityAuthorProfile(string? DisplayName, string? AvatarUrl)
{
    /// <summary>
    /// The stored author name as a display name: English full name preferred,
    /// Arabic fallback, and <c>null</c> when the identity row is missing or
    /// both names are blank — no placeholder name is invented.
    /// </summary>
    public static CommunityAuthorProfile FromAuthorName(CommunityAuthorName? name) =>
        new(DisplayNameOf(name), null);

    private static string? DisplayNameOf(CommunityAuthorName? name)
    {
        if (name is null)
        {
            return null;
        }

        string preferred = string.IsNullOrWhiteSpace(name.EnglishFullName)
            ? name.ArabicFullName
            : name.EnglishFullName;

        return string.IsNullOrWhiteSpace(preferred) ? null : preferred;
    }
}

/// <summary>
/// One batched identity lookup for the authors of a page of Community items.
/// The caller sends every distinct author id of the page at once; a per-row
/// lookup would be an N+1 and is never performed.
/// </summary>
public sealed record GetCommunityAuthorProfilesQuery(IReadOnlyCollection<Guid> UserIds)
    : IQuery<IReadOnlyDictionary<Guid, CommunityAuthorProfile>>;

public sealed class GetCommunityAuthorProfilesQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetCommunityAuthorProfilesQuery, IReadOnlyDictionary<Guid, CommunityAuthorProfile>>
{
    public async Task<Result<IReadOnlyDictionary<Guid, CommunityAuthorProfile>>> Handle(
        GetCommunityAuthorProfilesQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, CommunityAuthorName> names =
            await dbContext.GetCommunityAuthorNamesAsync(
                request.UserIds ?? [], cancellationToken);

        var profiles = new Dictionary<Guid, CommunityAuthorProfile>();
        foreach (KeyValuePair<Guid, CommunityAuthorName> entry in names)
        {
            profiles[entry.Key] = CommunityAuthorProfile.FromAuthorName(entry.Value);
        }

        return Result<IReadOnlyDictionary<Guid, CommunityAuthorProfile>>.Success(profiles);
    }
}
