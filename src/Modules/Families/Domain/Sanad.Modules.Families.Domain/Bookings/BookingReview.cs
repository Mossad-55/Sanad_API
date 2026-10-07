using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Bookings;

public sealed class BookingReview : Entity<BookingReviewId>
{
    public const int MaximumCommentLength = 2000;

    public BookingId BookingId { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }
    public bool IsAnonymous { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    private BookingReview()
    {
    }

    private BookingReview(
        BookingReviewId id,
        BookingId bookingId,
        UserId createdByUserId,
        int rating,
        string? comment,
        bool isAnonymous)
        : base(id)
    {
        if (bookingId == BookingId.Empty)
            throw new DomainException("Booking ID is required.");
        if (createdByUserId == UserId.Empty)
            throw new DomainException("Review author is required.");
        if (rating is < 1 or > 5)
            throw new DomainException("Rating must be between 1 and 5.");

        BookingId = bookingId;
        CreatedByUserId = createdByUserId;
        Rating = rating;
        Comment = NormalizeComment(comment);
        IsAnonymous = isAnonymous;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public static BookingReview Create(
        BookingId bookingId,
        UserId createdByUserId,
        int rating,
        string? comment,
        bool isAnonymous,
        DateTime createdOnUtc)
    {
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Review timestamp must be UTC.");

        var review = new BookingReview(
            BookingReviewId.New(), bookingId, createdByUserId, rating, comment, isAnonymous);
        review.CreatedOnUtc = createdOnUtc;
        return review;
    }

    private static string? NormalizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        string normalized = comment.Trim();
        if (normalized.Length > MaximumCommentLength)
            throw new DomainException($"Review comment cannot exceed {MaximumCommentLength} characters.");
        return normalized;
    }
}
