using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Application.Posts;

public sealed record GetPostQuery(Guid PostId) : IQuery<Post?>;

public sealed class GetPostQueryHandler
    : IQueryHandler<GetPostQuery, Post?>
{
    private readonly ICommunityDbContext _dbContext;

    public GetPostQueryHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Post?>> Handle(GetPostQuery request, CancellationToken cancellationToken)
    {
        Post? post = await _dbContext.Posts.AsNoTracking()
            .SingleOrDefaultAsync(
                post => post.Id == new Sanad.BuildingBlocks.Domain.Primitives.Ids.CommunityPostId(request.PostId) && post.Status == PostStatus.Published,
                cancellationToken);
        return Result<Post?>.Success(post);
    }
}
