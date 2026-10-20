using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

/// <summary>
/// F7 visibility state of a Care Home rating. Identical semantics to the
/// caregiver target: Visible is the default and the only state a family write
/// can produce; Hidden is set and cleared exclusively by the attributed admin
/// moderation actions.
/// </summary>
public enum CareHomeRatingModerationState
{
    Visible = 0,
    Hidden = 1
}

public sealed class CareHomeRating : Entity<Guid>
{
    public const int MaximumReviewTextLength = 2000;

    /// <summary>F7 bound on the attributed hide/restore reason.</summary>
    public const int MaximumModerationReasonLength = 500;

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

    /// <summary>
    /// F7 moderation state. A family edit never changes it: a re-rated hidden
    /// rating stays hidden.
    /// </summary>
    public CareHomeRatingModerationState ModerationState { get; private set; } = CareHomeRatingModerationState.Visible;

    public UserId? HiddenByUserId { get; private set; }
    public DateTime? HiddenOnUtc { get; private set; }
    public string? HideReason { get; private set; }

    public bool IsHidden => ModerationState == CareHomeRatingModerationState.Hidden;

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

    /// <summary>
    /// F7 hide. Never mutates stars, text or authorship; an already-hidden
    /// rating is a caller error (mapped to 409), never a silent no-op.
    /// </summary>
    public void Hide(UserId actor, string reason, DateTime utcNow)
    {
        if (IsHidden)
            throw new InvalidOperationException("CareHomes.Rating.AlreadyHidden");
        if (actor == UserId.Empty || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid actor and UTC timestamp are required.");

        ModerationState = CareHomeRatingModerationState.Hidden;
        HiddenByUserId = actor;
        HiddenOnUtc = utcNow;
        HideReason = NormalizeModerationReason(reason);
    }

    /// <summary>
    /// F7 restore. Returns the rating to every non-admin read and aggregate;
    /// the reason is validated here and retained only in the append-only
    /// moderation action record.
    /// </summary>
    public void Restore(UserId actor, string reason, DateTime utcNow)
    {
        if (!IsHidden)
            throw new InvalidOperationException("CareHomes.Rating.AlreadyVisible");
        if (actor == UserId.Empty || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid actor and UTC timestamp are required.");

        _ = NormalizeModerationReason(reason);

        ModerationState = CareHomeRatingModerationState.Visible;
        HiddenByUserId = null;
        HiddenOnUtc = null;
        HideReason = null;
    }

    private static string NormalizeModerationReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A moderation reason is required.");

        string normalized = reason.Trim();
        if (normalized.Length > MaximumModerationReasonLength)
            throw new ArgumentException("Moderation reason exceeds the maximum length.");

        return normalized;
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
