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
        Post? post = await _dbContext.Posts.AsNoTracking()
            .Include(p => p.Category)
            .SingleOrDefaultAsync(
                post => post.Id == new CommunityPostId(request.PostId) && post.Status == PostStatus.Published,
                cancellationToken);

        if (post is null)
            return Result<PostFeedItem?>.Success(null);

        var state = await PostViewerState.LoadAsync(
            _dbContext, request.ViewerUserId ?? Guid.Empty, [post], cancellationToken);

        return Result<PostFeedItem?>.Success(
            PostViewerState.Attach([post], state)[0]);
    }
}
