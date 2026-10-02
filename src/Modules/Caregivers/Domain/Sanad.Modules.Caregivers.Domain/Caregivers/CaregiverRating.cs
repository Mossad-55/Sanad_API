using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverRating : Entity<Guid>
{
    public const int MaximumReviewTextLength = 2000;

    private CaregiverRating()
    {
    }

    private CaregiverRating(
        Guid id,
        CaregiverId caregiverId,
        Guid bookingId,
        FamilyId familyId,
        UserId createdByUserId,
        int stars,
        string? reviewText,
        DateTime createdOnUtc)
        : base(id)
    {
        CaregiverId = caregiverId;
        BookingId = bookingId;
        FamilyId = familyId;
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        Stars = stars;
        ReviewText = reviewText;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public CaregiverId CaregiverId { get; private set; }
    public Guid BookingId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public UserId UpdatedByUserId { get; private set; }
    public int Stars { get; private set; }
    public string? ReviewText { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static CaregiverRating Create(
        CaregiverId caregiverId,
        Guid bookingId,
        FamilyId familyId,
        UserId actorUserId,
        int stars,
        string? reviewText,
        DateTime utcNow)
    {
        if (caregiverId == CaregiverId.Empty)
            throw new DomainException("Caregiver ID is required.");
        if (bookingId == Guid.Empty)
            throw new DomainException("Completed booking ID is required.");
        if (familyId == FamilyId.Empty)
            throw new DomainException("Family ID is required.");
        if (actorUserId == UserId.Empty)
            throw new DomainException("Rating actor is required.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Rating timestamps must be UTC.");

        return new CaregiverRating(
            Guid.NewGuid(),
            caregiverId,
            bookingId,
            familyId,
            actorUserId,
            ValidateStars(stars),
            NormalizeReviewText(reviewText),
            utcNow);
    }

    public void Edit(
        UserId actorUserId,
        int stars,
        string? reviewText,
        DateTime utcNow)
    {
        if (actorUserId == UserId.Empty)
            throw new DomainException("Rating actor is required.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Rating timestamps must be UTC.");

        Stars = ValidateStars(stars);
        ReviewText = NormalizeReviewText(reviewText);
        UpdatedByUserId = actorUserId;
        UpdatedOnUtc = utcNow;
    }

    private static int ValidateStars(int stars)
    {
        if (stars is < 1 or > 5)
            throw new DomainException("Rating must be between 1 and 5 stars.");

        return stars;
    }

    private static string? NormalizeReviewText(string? reviewText)
    {
        if (string.IsNullOrWhiteSpace(reviewText))
            return null;

        string normalized = reviewText.Trim();
        if (normalized.Length > MaximumReviewTextLength)
            throw new DomainException(
                $"Review text cannot exceed {MaximumReviewTextLength} characters.");

        return normalized;
    }
}
