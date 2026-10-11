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

        query = request.Sort switch
        {
            CommunityPostsSort.Oldest =>
                query.OrderBy(p => p.CreatedOnUtc).ThenBy(p => p.Id.Value),
            CommunityPostsSort.MostLiked =>
                query.OrderByDescending(p => p.LikesCount)
                    .ThenByDescending(p => p.CreatedOnUtc)
                    .ThenByDescending(p => p.Id.Value),
            _ =>
                query.OrderByDescending(p => p.CreatedOnUtc).ThenByDescending(p => p.Id.Value)
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
