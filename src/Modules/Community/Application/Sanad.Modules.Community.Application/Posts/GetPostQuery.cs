using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record GetPostQuery(Guid PostId, Guid? ViewerUserId = null) : IQuery<PostFeedItem?>;

public sealed class GetPostQueryHandler
    : IQueryHandler<GetPostQuery, PostFeedItem?>
{
    private readonly ICommunityDbContext _dbContext;

    public GetPostQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PostFeedItem?>> Handle(GetPostQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Post> query = _dbContext.Posts.AsNoTracking()
            .Include(p => p.Category)
            .Where(post =>
                post.Id == new CommunityPostId(request.PostId) &&
                post.Status == PostStatus.Published);

        // A post of an author the viewer blocked is not found — the same
        // not-found shape as an unknown or unpublished post. The predicate is
        // keyed on the stored author id and never exposes it.
        if (request.ViewerUserId is Guid viewerId && viewerId != Guid.Empty)
        {
            UserId viewerUserId = new(viewerId);
            query = query.Where(post => !_dbContext.CommunityAuthorBlocks.Any(
                block =>
                    block.BlockerUserId == viewerUserId &&
                    block.BlockedAuthorUserId == post.AuthorId));
        }

        Post? post = await query.SingleOrDefaultAsync(cancellationToken);

        if (post is null)
            return Result<PostFeedItem?>.Success(null);

        var state = await PostViewerState.LoadAsync(
            _dbContext, request.ViewerUserId ?? Guid.Empty, [post], cancellationToken);

        return Result<PostFeedItem?>.Success(
            PostViewerState.Attach([post], state)[0]);
    }
}
