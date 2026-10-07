using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
namespace Sanad.Modules.Community.Application.Ratings;

public sealed record PostRatingAverage(Guid PostId, double AverageRating, int RatingCount);

public sealed record GetPostRatingAverageQuery(Guid PostId) : IQuery<PostRatingAverage>;

public sealed class GetPostRatingAverageQueryHandler
    (ICommunityDbContext dbContext) : IQueryHandler<GetPostRatingAverageQuery, PostRatingAverage>
{
    public async Task<Result<PostRatingAverage>> Handle(GetPostRatingAverageQuery request, CancellationToken cancellationToken)
    {
        var ratings = await dbContext.Ratings
            .Where(r => r.PostId == new CommunityPostId(request.PostId))
            .ToListAsync(cancellationToken);

        if (ratings.Count == 0)
        {
            return Result<PostRatingAverage>.Success(
                new PostRatingAverage(request.PostId, 0, 0));
        }

        var average = ratings.Average(r => double.Parse(r.RatingValue));
        
        return Result<PostRatingAverage>.Success(
            new PostRatingAverage(request.PostId, Math.Round(average, 1), ratings.Count));
    }
}
