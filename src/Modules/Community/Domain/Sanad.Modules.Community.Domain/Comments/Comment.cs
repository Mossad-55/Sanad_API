using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Domain.Comments;

public sealed class Comment : AggregateRoot<CommunityCommentId>
{
    private readonly List<Reply> _replies = [];

    private Comment()
    {
    }

    private Comment(
        CommunityCommentId id,
        CommunityPostId postId,
        UserId authorId,
        string contentArabic,
        string contentEnglish,
        DateTime createdOnUtc)
        : base(id)
    {
        if (postId == CommunityPostId.Empty)
            throw new DomainException("A Community comment requires a post.");
        if (authorId == UserId.Empty)
            throw new DomainException("A Community comment requires an author.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community comment timestamps must be UTC.");

        PostId = postId;
        AuthorId = authorId;
        ContentArabic = RequiredContent(contentArabic, nameof(contentArabic));
        ContentEnglish = RequiredContent(contentEnglish, nameof(contentEnglish));
        CreatedOnUtc = createdOnUtc;
    }

    public CommunityPostId PostId { get; private set; }
    public UserId AuthorId { get; private set; }
    public string ContentArabic { get; private set; } = string.Empty;
    public string ContentEnglish { get; private set; } = string.Empty;
    public bool IsEdited { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }
    public int LikesCount { get; private set; }
    public Post Post { get; private set; } = null!;
    public IReadOnlyList<Reply> Replies => _replies.AsReadOnly();

    public static Comment Create(
        CommunityPostId postId,
        UserId authorId,
        string contentArabic,
        string contentEnglish,
        DateTime createdOnUtc) =>
        new(CommunityCommentId.New(), postId, authorId,
            contentArabic, contentEnglish, createdOnUtc);

    public bool IsOwnedBy(UserId userId) => AuthorId == userId;

    public void UpdateContent(string contentArabic, string contentEnglish, DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("Community comment timestamps must be UTC.");
        ContentArabic = RequiredContent(contentArabic, nameof(contentArabic));
        ContentEnglish = RequiredContent(contentEnglish, nameof(contentEnglish));
        IsEdited = true;
        UpdatedOnUtc = utcNow;
    }

    public void IncrementLikes() => LikesCount = checked(LikesCount + 1);
    public void DecrementLikes() => LikesCount = Math.Max(0, LikesCount - 1);

    private static string RequiredContent(string? value, string name)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 500)
            throw new DomainException($"{name} must contain between 1 and 500 characters.");
        return normalized;
    }
}

public sealed class Reply : Entity<CommunityReplyId>
{
    private Reply()
    {
    }

    private Reply(
        CommunityReplyId id,
        CommunityCommentId commentId,
        UserId authorId,
        string contentArabic,
        string contentEnglish,
        DateTime createdOnUtc)
        : base(id)
    {
        if (commentId == CommunityCommentId.Empty)
            throw new DomainException("A Community reply requires a comment.");
        if (authorId == UserId.Empty)
            throw new DomainException("A Community reply requires an author.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community reply timestamps must be UTC.");

        CommentId = commentId;
        AuthorId = authorId;
        ContentArabic = RequiredContent(contentArabic, nameof(contentArabic));
        ContentEnglish = RequiredContent(contentEnglish, nameof(contentEnglish));
        CreatedOnUtc = createdOnUtc;
    }

    public CommunityCommentId CommentId { get; private set; }
    public UserId AuthorId { get; private set; }
    public string ContentArabic { get; private set; } = string.Empty;
    public string ContentEnglish { get; private set; } = string.Empty;
    public bool IsEdited { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }
    public int LikesCount { get; private set; }

    public static Reply Create(
        CommunityCommentId commentId,
        UserId authorId,
        string contentArabic,
        string contentEnglish,
        DateTime createdOnUtc) =>
        new(CommunityReplyId.New(), commentId, authorId,
            contentArabic, contentEnglish, createdOnUtc);

    private static string RequiredContent(string? value, string name)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 500)
            throw new DomainException($"{name} must contain between 1 and 500 characters.");
        return normalized;
    }
}
