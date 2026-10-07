using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.UnitTests.Families;

public sealed class BookingReviewTests
{
    [Fact]
    public void Create_RejectsRatingOutsideOneThroughFive()
    {
        Assert.Throws<DomainException>(() => BookingReview.Create(
            BookingId.New(), UserId.New(), 0, null, false, DateTime.UtcNow));
    }

    [Fact]
    public void Create_NormalizesCommentAndStoresTypedAuthor()
    {
        var author = UserId.New();
        var review = BookingReview.Create(
            BookingId.New(), author, 5, "  Great care  ", false, DateTime.UtcNow);

        Assert.Equal(author, review.CreatedByUserId);
        Assert.Equal("Great care", review.Comment);
    }

    [Fact]
    public void Create_RejectsCommentBeyondMaximumLength()
    {
        Assert.Throws<DomainException>(() => BookingReview.Create(
            BookingId.New(), UserId.New(), 4,
            new string('x', BookingReview.MaximumCommentLength + 1), false, DateTime.UtcNow));
    }
}
