using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Domain.Posts;

public sealed class Post : AggregateRoot<CommunityPostId>
{
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
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }
    public string? PublishedBy { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }

    public static Post Create(
        string titleArabic,
        string titleEnglish,
        string contentArabic,
        string contentEnglish,
        string? imageUrl,
        bool isAnonymous,
        UserId authorId,
        DateTime createdOnUtc) =>
        new(CommunityPostId.New(), titleArabic, titleEnglish, contentArabic,
            contentEnglish, imageUrl, isAnonymous, authorId, createdOnUtc);

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
    }

    public void Reject(DateTime rejectedOnUtc)
    {
        if (rejectedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Rejection timestamps must be UTC.");
        if (Status != PostStatus.PendingReview)
            throw new DomainException("Only posts pending review can be rejected.");

        Status = PostStatus.Rejected;
        UpdatedOnUtc = rejectedOnUtc;
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
}

public enum PostStatus
{
    Draft = 0,
    PendingReview = 1,
    Published = 2,
    Rejected = 3,
    Archived = 4
}
