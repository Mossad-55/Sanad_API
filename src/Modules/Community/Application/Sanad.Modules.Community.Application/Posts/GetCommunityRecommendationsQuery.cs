using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;
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
        var userId = new UserId(request.UserId);

        List<Guid> interactedIds = await InteractedPostIdsAsync(userId, cancellationToken);
        List<CommunityPostId> interacted = interactedIds
            .Select(id => new CommunityPostId(id))
            .ToList();

        List<UserId> affinityAuthors = await _dbContext.Posts
            .AsNoTracking()
            .Where(p => interacted.Contains(p.Id) && p.AuthorId != userId)
            .Select(p => p.AuthorId)
            .Distinct()
            .ToListAsync(cancellationToken);

        IReadOnlyList<Post> posts = await _dbContext.Posts
            .AsNoTracking()
            .Where(p => p.Status == PostStatus.Published && !interacted.Contains(p.Id))
            .OrderByDescending(p => affinityAuthors.Contains(p.AuthorId))
            .ThenByDescending(p => p.CreatedOnUtc)
            .ThenByDescending(p => p.LikesCount + p.FavoritesCount + p.CommentsCount)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<Post>>.Success(posts);
    }

    private async Task<List<Guid>> InteractedPostIdsAsync(UserId userId, CancellationToken cancellationToken)
    {
        List<Guid> interactionTargets = await _dbContext.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == userId && i.TargetType == CommunityInteractionTarget.Post)
            .Select(i => i.TargetId)
            .ToListAsync(cancellationToken);

        List<Guid> commented = (await _dbContext.Comments
            .AsNoTracking()
            .Where(c => c.AuthorId == userId)
            .Select(c => c.PostId)
            .ToListAsync(cancellationToken))
            .Select(p => p.Value)
            .ToList();

        List<Guid> rated = (await _dbContext.Ratings
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.PostId)
            .ToListAsync(cancellationToken))
            .Select(p => p.Value)
            .ToList();

        List<Guid> checkedIn = (await _dbContext.CheckIns
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.PostId)
            .ToListAsync(cancellationToken))
            .Select(p => p.Value)
            .ToList();

        return interactionTargets
            .Concat(commented)
            .Concat(rated)
            .Concat(checkedIn)
            .Distinct()
            .ToList();
    }
}
