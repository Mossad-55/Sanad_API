using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// F7 visibility state of a caregiver rating. Visible is the default and the
/// only state a family write can produce; Hidden is set and cleared
/// exclusively by the attributed admin moderation actions.
/// </summary>
public enum RatingModerationState
{
    Visible = 0,
    Hidden = 1
}

public sealed class CaregiverRating : Entity<Guid>
{
    public const int MaximumReviewTextLength = 2000;

    /// <summary>F7 bound on the attributed hide/restore reason.</summary>
    public const int MaximumModerationReasonLength = 500;

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

    /// <summary>
    /// F7 moderation state. Always <see cref="RatingModerationState.Visible"/>
    /// on create and after an edit, because a family re-rating never changes
    /// moderation (a re-rated hidden rating stays hidden).
    /// </summary>
    public RatingModerationState ModerationState { get; private set; } = RatingModerationState.Visible;

    public UserId? HiddenByUserId { get; private set; }
    public DateTime? HiddenOnUtc { get; private set; }
    public string? HideReason { get; private set; }

    public bool IsHidden => ModerationState == RatingModerationState.Hidden;

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

    /// <summary>
    /// F7 hide. Hiding never mutates stars, text or authorship: it only makes
    /// the rating invisible to non-admin reads and excludes it from every
    /// aggregate until <see cref="Restore"/>. An already-hidden rating is a
    /// caller error (mapped to 409), never a silent no-op.
    /// </summary>
    public void Hide(UserId actorUserId, string reason, DateTime utcNow)
    {
        if (IsHidden)
            throw new DomainException("The rating is already hidden.");
        if (actorUserId == UserId.Empty)
            throw new DomainException("Rating moderator is required.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Rating timestamps must be UTC.");

        string normalizedReason = NormalizeModerationReason(reason);

        ModerationState = RatingModerationState.Hidden;
        HiddenByUserId = actorUserId;
        HiddenOnUtc = utcNow;
        HideReason = normalizedReason;
    }

    /// <summary>
    /// F7 restore. Returns the rating to every non-admin read and aggregate;
    /// the reason is validated here and retained only in the append-only
    /// moderation action record.
    /// </summary>
    public void Restore(UserId actorUserId, string reason, DateTime utcNow)
    {
        if (!IsHidden)
            throw new DomainException("The rating is already visible.");
        if (actorUserId == UserId.Empty)
            throw new DomainException("Rating moderator is required.");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Rating timestamps must be UTC.");

        _ = NormalizeModerationReason(reason);

        ModerationState = RatingModerationState.Visible;
        HiddenByUserId = null;
        HiddenOnUtc = null;
        HideReason = null;
    }

    private static string NormalizeModerationReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A moderation reason is required.");

        string normalized = reason.Trim();
        if (normalized.Length > MaximumModerationReasonLength)
            throw new DomainException(
                $"Moderation reason cannot exceed {MaximumModerationReasonLength} characters.");

        return normalized;
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
