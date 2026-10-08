using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public sealed class CareHomeRating : Entity<Guid>
{
    public const int MaximumReviewTextLength = 2000;

    private CareHomeRating() { }

    private CareHomeRating(Guid id, Guid bookingId, CareHomeId careHomeId, FamilyId familyId,
        UserId actor, int stars, string? reviewText, DateTime utcNow) : base(id)
    {
        BookingId = bookingId;
        CareHomeId = careHomeId;
        FamilyId = familyId;
        CreatedByUserId = actor;
        UpdatedByUserId = actor;
        Stars = stars;
        ReviewText = Normalize(reviewText);
        CreatedOnUtc = UpdatedOnUtc = utcNow;
    }

    public Guid BookingId { get; private set; }
    public CareHomeId CareHomeId { get; private set; }
    public FamilyId FamilyId { get; private set; }
    public UserId CreatedByUserId { get; private set; }
    public UserId UpdatedByUserId { get; private set; }
    public int Stars { get; private set; }
    public string? ReviewText { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static CareHomeRating Create(Guid bookingId, CareHomeId careHomeId, FamilyId familyId,
        UserId actor, int stars, string? reviewText, DateTime utcNow)
    {
        Validate(bookingId, careHomeId, familyId, actor, stars, utcNow);
        return new(Guid.NewGuid(), bookingId, careHomeId, familyId, actor, stars, reviewText, utcNow);
    }

    public void Edit(UserId actor, int stars, string? reviewText, DateTime utcNow)
    {
        if (actor == UserId.Empty || stars is < 1 or > 5 || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid actor, one-to-five star value, and UTC timestamp are required.");
        Stars = stars;
        ReviewText = Normalize(reviewText);
        UpdatedByUserId = actor;
        UpdatedOnUtc = utcNow;
    }

    private static void Validate(Guid bookingId, CareHomeId careHomeId, FamilyId familyId,
        UserId actor, int stars, DateTime utcNow)
    {
        if (bookingId == Guid.Empty || careHomeId == CareHomeId.Empty || familyId == FamilyId.Empty ||
            actor == UserId.Empty || stars is < 1 or > 5 || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid booking, care home, Family, actor, one-to-five star value, and UTC timestamp are required.");
    }

    private static string? Normalize(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumReviewTextLength)
            throw new ArgumentException("Review text exceeds the maximum length.");
        return normalized;
    }
}
