using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Categories;

namespace Sanad.Modules.Community.Domain.Posts;

public sealed class Post : AggregateRoot<CommunityPostId>
{
    public const int MaximumNoteLength = 1000;

    private Post()
    {
    }

    private Post(
        CommunityPostId id,
        string titleArabic,
        string titleEnglish,
        string contentArabic,
        string contentEnglish,
        string? imageUrl,
        bool isAnonymous,
        UserId authorId,
        CommunityCategoryId? categoryId,
        DateTime createdOnUtc)
        : base(id)
    {
        TitleArabic = RequiredText(titleArabic, nameof(titleArabic), 200);
        TitleEnglish = RequiredText(titleEnglish, nameof(titleEnglish), 200);
        ContentArabic = RequiredText(contentArabic, nameof(contentArabic));
        ContentEnglish = RequiredText(contentEnglish, nameof(contentEnglish));
        ImageUrl = OptionalText(imageUrl, nameof(imageUrl), 500);
        if (authorId == UserId.Empty)
            throw new DomainException("Community post author is required.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community post timestamps must be UTC.");

        IsAnonymous = isAnonymous;
        AuthorId = authorId;
        CategoryId = categoryId;
        Status = PostStatus.PendingReview;
        CreatedOnUtc = createdOnUtc;
    }

    public string TitleArabic { get; private set; } = string.Empty;
    public string TitleEnglish { get; private set; } = string.Empty;
    public string ContentArabic { get; private set; } = string.Empty;
    public string ContentEnglish { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public bool IsAnonymous { get; private set; }
    public PostStatus Status { get; private set; }
    public int LikesCount { get; private set; }
    public int CommentsCount { get; private set; }
    public int FavoritesCount { get; private set; }
    public UserId AuthorId { get; private set; }

    /// <summary>
    /// Optimistic-concurrency token for the post's lifecycle state (the same
    /// convention <see cref="CommunityPostReport.Version"/> uses): an
    /// author edit that races a concurrent deletion — or a moderation
    /// transition from another context — fails with a stale-write conflict
    /// instead of resurrecting the post or losing the deletion.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Deletion audit: the authenticated actor that deleted the post
    /// (the author on the app route, a CommunityModeration moderator on the
    /// Admin route).</summary>
    public UserId? DeletedBy { get; private set; }

    /// <summary>Deletion audit: when the post was deleted (UTC).</summary>
    public DateTime? DeletedOnUtc { get; private set; }

    public CommunityCategoryId? CategoryId { get; private set; }
    public CommunityCategory? Category { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }
    public string? PublishedBy { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }
    public UserId? RejectedBy { get; private set; }
    public string? RejectionReasonCode { get; private set; }
    public string? RejectionNote { get; private set; }
    public DateTime? RejectedOnUtc { get; private set; }

    public static Post Create(
        string titleArabic,
        string titleEnglish,
        string contentArabic,
        string contentEnglish,
        string? imageUrl,
        bool isAnonymous,
        UserId authorId,
        DateTime createdOnUtc,
        CommunityCategoryId? categoryId = null) =>
        new(CommunityPostId.New(), titleArabic, titleEnglish, contentArabic,
            contentEnglish, imageUrl, isAnonymous, authorId, categoryId, createdOnUtc);

    public void IncrementLikes() => LikesCount = checked(LikesCount + 1);
    public void DecrementLikes() => LikesCount = Math.Max(0, LikesCount - 1);
    public void IncrementComments() => CommentsCount = checked(CommentsCount + 1);
    public void DecrementComments() => CommentsCount = Math.Max(0, CommentsCount - 1);
    public void IncrementFavorites() => FavoritesCount = checked(FavoritesCount + 1);
    public void DecrementFavorites() => FavoritesCount = Math.Max(0, FavoritesCount - 1);

    public void Publish(UserId moderatorId, DateTime publishedOnUtc)
    {
        if (moderatorId == UserId.Empty)
            throw new DomainException("A publishing moderator is required.");
        if (publishedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Publication timestamps must be UTC.");
        if (Status != PostStatus.PendingReview)
            throw new DomainException("Only posts pending review can be published.");

        Status = PostStatus.Published;
        PublishedBy = moderatorId.Value.ToString();
        PublishedOnUtc = publishedOnUtc;
        UpdatedOnUtc = publishedOnUtc;
        Version = checked(Version + 1);
    }

    public void Reject(
        UserId moderatorId,
        string? reasonCode,
        string? note,
        DateTime rejectedOnUtc)
    {
        if (moderatorId == UserId.Empty)
            throw new DomainException("A rejecting moderator is required.");
        if (!CommunityRejectionReasonCodes.IsValid(reasonCode))
            throw new DomainException("Community rejection reason code is invalid.");
        string code = reasonCode!;
        string? normalizedNote = NormalizeRejectionNote(note);
        if (CommunityRejectionReasonCodes.RequiresNote(code) && normalizedNote is null)
            throw new DomainException("A note is required for this rejection reason.");
        if (rejectedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Rejection timestamps must be UTC.");
        if (Status != PostStatus.PendingReview)
            throw new DomainException("Only posts pending review can be rejected.");

        Status = PostStatus.Rejected;
        RejectedBy = moderatorId;
        RejectionReasonCode = code;
        RejectionNote = normalizedNote;
        RejectedOnUtc = rejectedOnUtc;
        UpdatedOnUtc = rejectedOnUtc;
        Version = checked(Version + 1);
    }

    /// <summary>
    /// The authenticated author edits their own post. The author identity is
    /// immutable — only the content fields, the image reference, the category
    /// and the anonymity choice can change.
    /// <para>
    /// Editing never bypasses the existing approval requirement: a post that
    /// is already <see cref="PostStatus.Published"/> returns to
    /// <see cref="PostStatus.PendingReview"/> with its publication metadata
    /// cleared, so a moderator re-approves the new content. A rejected or
    /// archived post is no longer editable.
    /// </para>
    /// </summary>
    public void Update(
        string titleArabic,
        string titleEnglish,
        string contentArabic,
        string contentEnglish,
        string? imageUrl,
        bool isAnonymous,
        CommunityCategoryId? categoryId,
        DateTime updatedOnUtc)
    {
        if (updatedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community post timestamps must be UTC.");
        if (Status is not (PostStatus.PendingReview or PostStatus.Published))
            throw new DomainException("Only a pending-review or published post can be edited by its author.");

        TitleArabic = RequiredText(titleArabic, nameof(titleArabic), 200);
        TitleEnglish = RequiredText(titleEnglish, nameof(titleEnglish), 200);
        ContentArabic = RequiredText(contentArabic, nameof(contentArabic));
        ContentEnglish = RequiredText(contentEnglish, nameof(contentEnglish));
        ImageUrl = OptionalText(imageUrl, nameof(imageUrl), 500);
        IsAnonymous = isAnonymous;
        CategoryId = categoryId;

        if (Status == PostStatus.Published)
        {
            Status = PostStatus.PendingReview;
            PublishedBy = null;
            PublishedOnUtc = null;
        }

        UpdatedOnUtc = updatedOnUtc;
        Version = checked(Version + 1);
    }

    /// <summary>
    /// Deletes the post with a retained audit trail: the row, its comments,
    /// replies and the referenced media stay in the database — deletion is the
    /// <see cref="PostStatus.Deleted"/> transition, never a cascading hard
    /// delete of children, media or unrelated data.
    /// </summary>
    public void Delete(UserId deletedBy, DateTime deletedOnUtc)
    {
        if (deletedBy == UserId.Empty)
            throw new DomainException("A deleting actor is required.");
        if (deletedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Deletion timestamps must be UTC.");
        if (Status == PostStatus.Deleted)
            throw new DomainException("The post is already deleted.");

        Status = PostStatus.Deleted;
        DeletedBy = deletedBy;
        DeletedOnUtc = deletedOnUtc;
        UpdatedOnUtc = deletedOnUtc;
        Version = checked(Version + 1);
    }

    private static string RequiredText(string? value, string name, int? maximumLength = null)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || maximumLength.HasValue && normalized.Length > maximumLength.Value)
            throw new DomainException($"{name} is required and must not exceed {maximumLength?.ToString() ?? "the allowed"} characters.");
        return normalized;
    }

    private static string? OptionalText(string? value, string name, int maximumLength)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maximumLength)
            throw new DomainException($"{name} must not exceed {maximumLength} characters.");
        return normalized;
    }

    private static string? NormalizeRejectionNote(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumNoteLength)
            throw new DomainException(
                $"Community rejection notes must not exceed {MaximumNoteLength} characters.");
        return normalized;
    }
}

public enum PostStatus
{
    Draft = 0,
    PendingReview = 1,
    Published = 2,
    Rejected = 3,
    Archived = 4,

    /// <summary>Deleted by its author (app route) or by a Community-moderation
    /// administrator (Admin route). Deleted posts are hidden from every
    /// public read and reject new child interactions, while the row itself
    /// retains the moderation/audit trail.</summary>
    Deleted = 5
}

/// <summary>
/// The closed set of FUR-07 rejection reason codes. Unknown codes are
/// validation errors. <see cref="Other"/> requires a note; every other code
/// accepts an optional note.
/// </summary>
public static class CommunityRejectionReasonCodes
{
    public const string CommunityGuidelines = "CommunityGuidelines";
    public const string Spam = "Spam";
    public const string Duplicate = "Duplicate";
    public const string UnsafeContent = "UnsafeContent";
    public const string Other = "Other";

    public static readonly int MaximumLength = 50;

    private static readonly HashSet<string> All =
        new(StringComparer.Ordinal)
        {
            CommunityGuidelines,
            Spam,
            Duplicate,
            UnsafeContent,
            Other
        };

    public static bool IsValid(string? reasonCode) =>
        reasonCode is not null && All.Contains(reasonCode);

    public static bool RequiresNote(string? reasonCode) =>
        string.Equals(reasonCode, Other, StringComparison.Ordinal);
}
