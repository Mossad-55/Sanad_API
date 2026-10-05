using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Application.Ratings;

public sealed class AddRatingCommandHandler : ICommandHandler<AddRatingCommand, bool>
{
    private readonly ICommunityDbContext _dbContext;

    public AddRatingCommandHandler(ICommunityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<bool>> Handle(AddRatingCommand request, CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty ||
            !int.TryParse(request.RatingValue, out var ratingValue) || ratingValue is < 1 or > 5)
        {
            return false;
        }

        var post = await _dbContext.Posts.FindAsync([new CommunityPostId(request.PostId)], cancellationToken);

        if (post is null || post.Status != PostStatus.Published)
        {
            return false;
        }

        // Check if user already rated this post
        var existingRating = await _dbContext.Ratings
            .FirstOrDefaultAsync(r => r.PostId == new CommunityPostId(request.PostId) && r.UserId == new UserId(request.UserId));

        if (existingRating is not null)
        {
            // Update existing rating
            existingRating.ChangeValue(ratingValue, DateTime.UtcNow);
        }
        else
        {
            // Create new rating
            _dbContext.Ratings.Add(Rating.Create(
                new CommunityPostId(request.PostId),
                new UserId(request.UserId),
                ratingValue,
                DateTime.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
