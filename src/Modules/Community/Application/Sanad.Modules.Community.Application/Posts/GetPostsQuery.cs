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
/// <c>AvatarUrl</c> is the **downloadable** path of the author's avatar when a
/// stored one exists, and <c>null</c> otherwise. It is a route, never the
/// stored Identity private storage key: the bytes are served only by the
/// authorized Community author-avatar route, which enforces the anonymity rule
/// (an anonymous author is never served). The stored key stays server-side and
/// is never serialized. When no avatar is stored the honest value remains
/// <c>null</c>.
/// </para>
/// </summary>
public sealed record CommunityAuthorProfile(string? DisplayName, string? AvatarUrl)
{
    /// <summary>
    /// The authorized download path of an author's avatar. It carries no key
    /// material — only the author id the route already knows — so an anonymous
    /// author resolving to this path still leaks nothing (the route resolves
    /// anonymity independently and answers 404 for it).
    /// </summary>
    public static string AvatarPathFor(Guid authorId) =>
        $"/api/v1/community/authors/{authorId}/photo";

    /// <summary>
    /// The stored author name as a display name: English full name preferred,
    /// Arabic fallback, and <c>null</c> when the identity row is missing or
    /// both names are blank — no placeholder name is invented. The avatar path
    /// is published only when the identity row carries a stored avatar key.
    /// </summary>
    public static CommunityAuthorProfile FromAuthorName(CommunityAuthorName? name) =>
        new(
            DisplayNameOf(name),
            string.IsNullOrWhiteSpace(name?.AvatarKey) ? null : AvatarPathFor(UserIdOf(name!)));

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

    private static Guid UserIdOf(CommunityAuthorName name) => name.UserId;
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

        // One privacy predicate for every consumer (post, comment and reply
        // projections): the avatar path is published only for authors whose
        // avatar the authorized download route would actually serve. Anything
        // else degrades to an honest null instead of a URL that answers 404.
        HashSet<Guid> photoVisible =
            await LoadPhotoVisibleAuthorIdsAsync(
                dbContext,
                (request.UserIds ?? []).Distinct().ToArray(),
                cancellationToken);

        var profiles = new Dictionary<Guid, CommunityAuthorProfile>();
        foreach (KeyValuePair<Guid, CommunityAuthorName> entry in names)
        {
            CommunityAuthorProfile profile = CommunityAuthorProfile.FromAuthorName(entry.Value);
            profiles[entry.Key] = photoVisible.Contains(entry.Key)
                ? profile
                : profile with { AvatarUrl = null };
        }

        return Result<IReadOnlyDictionary<Guid, CommunityAuthorProfile>>.Success(profiles);
    }

    /// <summary>
    /// The authors the avatar download route serves:
    /// <list type="bullet">
    /// <item>at least one <see cref="PostStatus.Published"/>, non-anonymous
    /// post (a real, visible Community author), and</item>
    /// <item>no anonymous post ever (the conservative D7 anonymity rule — any
    /// anonymous-post history suppresses that author's photo everywhere).</item>
    /// </list>
    /// A deleted post stops being published, so an author whose only posts
    /// were deleted loses the avatar path on the next read — the same answer
    /// the download route gives. Two bounded set queries for the whole batch,
    /// never one per author.
    /// </summary>
    private static async Task<HashSet<Guid>> LoadPhotoVisibleAuthorIdsAsync(
        ICommunityDbContext dbContext,
        Guid[] authorIds,
        CancellationToken cancellationToken)
    {
        if (authorIds.Length == 0)
        {
            return [];
        }

        List<UserId> ids = authorIds.Select(id => new UserId(id)).ToList();

        List<UserId> publishedVisible = await dbContext.Posts
            .AsNoTracking()
            .Where(post =>
                ids.Contains(post.AuthorId) &&
                post.Status == PostStatus.Published &&
                !post.IsAnonymous)
            .Select(post => post.AuthorId)
            .ToListAsync(cancellationToken);

        List<UserId> everAnonymous = await dbContext.Posts
            .AsNoTracking()
            .Where(post => ids.Contains(post.AuthorId) && post.IsAnonymous)
            .Select(post => post.AuthorId)
            .ToListAsync(cancellationToken);

        var hidden = everAnonymous.Select(id => id.Value).ToHashSet();
        return publishedVisible
            .Where(id => !hidden.Contains(id.Value))
            .Select(id => id.Value)
            .ToHashSet();
    }
}
