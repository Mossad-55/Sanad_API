using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Domain.Ratings;

public sealed class Rating : Entity<CommunityRatingId>
{
    private Rating()
    {
    }

    private Rating(
        CommunityRatingId id,
        CommunityPostId postId,
        UserId userId,
        int ratingValue,
        DateTime createdOnUtc)
        : base(id)
    {
        if (postId == CommunityPostId.Empty)
            throw new DomainException("A Community rating requires a post.");
        if (userId == UserId.Empty)
            throw new DomainException("A Community rating requires an author.");
        ValidateRating(ratingValue);
        ValidateUtc(createdOnUtc);

        PostId = postId;
        UserId = userId;
        RatingValue = ratingValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        CreatedOnUtc = createdOnUtc;
    }

    public CommunityPostId PostId { get; private set; }
    public UserId UserId { get; private set; }
    public string RatingValue { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }
    public Post Post { get; private set; } = null!;

    public static Rating Create(
        CommunityPostId postId,
        UserId userId,
        int ratingValue,
        DateTime utcNow) =>
        new(CommunityRatingId.New(), postId, userId, ratingValue, utcNow);

    public void ChangeValue(int ratingValue, DateTime utcNow)
    {
        ValidateRating(ratingValue);
        ValidateUtc(utcNow);
        RatingValue = ratingValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        CreatedOnUtc = utcNow;
    }

    private static void ValidateRating(int value)
    {
        if (value is < 1 or > 5)
            throw new DomainException("Community ratings must be between 1 and 5.");
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException("Community rating timestamps must be UTC.");
    }
}
