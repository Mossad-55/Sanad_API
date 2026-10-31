using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Cms.Domain.Library;

public sealed class LibraryContent : AggregateRoot<LibraryContentId>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumSummaryLength = 1000;
    public const int MaximumVideoUrlLength = 500;
    public const int MaximumVideoStorageKeyLength = 500;
    public const int MaximumCoverPathLength = 500;
    public const int MaximumSectionCount = 40;

    private readonly List<LibraryContentSection> _sections = [];

    private LibraryContent()
    {
    }

    private LibraryContent(
        LibraryContentId id,
        LibraryContentType type,
        LibraryCategoryId? categoryId,
        string arabicTitle,
        string englishTitle,
        string arabicSummary,
        string englishSummary,
        string? videoUrl,
        string? videoStorageKey,
        int? durationSeconds,
        string coverImagePath,
        bool isPremium,
        IReadOnlyList<LibraryContentSectionDraft> sections,
        DateTime createdOnUtc)
        : base(id)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException("Library content type is invalid.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Library content timestamps must be UTC.");

        Type = type;
        CategoryId = categoryId;
        ArabicTitle = RequiredText(arabicTitle, nameof(arabicTitle), MaximumTitleLength);
        EnglishTitle = RequiredText(englishTitle, nameof(englishTitle), MaximumTitleLength);
        ArabicSummary = RequiredText(arabicSummary, nameof(arabicSummary), MaximumSummaryLength);
        EnglishSummary = RequiredText(englishSummary, nameof(englishSummary), MaximumSummaryLength);
        CoverImagePath = RequiredText(coverImagePath, nameof(coverImagePath), MaximumCoverPathLength);
        IsPremium = isPremium;
        ApplyTypeRules(type, videoUrl, videoStorageKey, durationSeconds, sections);
        Status = LibraryContentStatus.Draft;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public LibraryContentType Type { get; private set; }
    public LibraryCategoryId? CategoryId { get; private set; }
    public LibraryCategory? Category { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string ArabicSummary { get; private set; } = string.Empty;
    public string EnglishSummary { get; private set; } = string.Empty;
    public string? VideoUrl { get; private set; }

    /// <summary>
    /// Private storage key of the uploaded video bytes, or <c>null</c> when no
    /// bytes are attached. Only <see cref="LibraryContentType.Video"/> content can
    /// carry one. <see cref="VideoUrl"/> stays the legacy administrator metadata
    /// and is never used as a playback source for consumers.
    /// </summary>
    public string? VideoStorageKey { get; private set; }

    public bool HasStoredVideo => !string.IsNullOrWhiteSpace(VideoStorageKey);
    public int? DurationSeconds { get; private set; }
    public string CoverImagePath { get; private set; } = string.Empty;
    public bool IsPremium { get; private set; }
    public LibraryContentStatus Status { get; private set; }
    public int ViewCount { get; private set; }
    public int LikesCount { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }
    public IReadOnlyList<LibraryContentSection> Sections => _sections;

    public static LibraryContent Create(
        LibraryContentType type,
        LibraryCategoryId? categoryId,
        string arabicTitle,
        string englishTitle,
        string arabicSummary,
        string englishSummary,
        string? videoUrl,
        string? videoStorageKey,
        int? durationSeconds,
        string coverImagePath,
        bool isPremium,
        IReadOnlyList<LibraryContentSectionDraft> sections,
        DateTime createdOnUtc) =>
        new(
            LibraryContentId.New(),
            type,
            categoryId,
            arabicTitle,
            englishTitle,
            arabicSummary,
            englishSummary,
            videoUrl,
            videoStorageKey,
            durationSeconds,
            coverImagePath,
            isPremium,
            sections ?? [],
            createdOnUtc);

    public void Update(
        LibraryCategoryId? categoryId,
        string arabicTitle,
        string englishTitle,
        string arabicSummary,
        string englishSummary,
        string? videoUrl,
        int? durationSeconds,
        string coverImagePath,
        bool isPremium,
        IReadOnlyList<LibraryContentSectionDraft> sections)
    {
        if (Status != LibraryContentStatus.Draft)
            throw new DomainException("Only draft library content can be modified.");

        CategoryId = categoryId;
        ArabicTitle = RequiredText(arabicTitle, nameof(arabicTitle), MaximumTitleLength);
        EnglishTitle = RequiredText(englishTitle, nameof(englishTitle), MaximumTitleLength);
        ArabicSummary = RequiredText(arabicSummary, nameof(arabicSummary), MaximumSummaryLength);
        EnglishSummary = RequiredText(englishSummary, nameof(englishSummary), MaximumSummaryLength);
        CoverImagePath = RequiredText(coverImagePath, nameof(coverImagePath), MaximumCoverPathLength);
        IsPremium = isPremium;

        // A metadata update never detaches already-uploaded bytes: the stored
        // video reference survives unless the explicit remove operation clears
        // it. Articles cannot address a video at all, so the reference is dropped
        // when the effective type is not Video.
        ApplyTypeRules(
            Type,
            videoUrl,
            Type == LibraryContentType.Video ? VideoStorageKey : null,
            durationSeconds,
            sections);
        UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Attaches (or replaces) the private storage reference of the uploaded video
    /// bytes. Draft and published content can receive bytes; archived content is
    /// inert. The legacy <see cref="VideoUrl"/> metadata is untouched, and no
    /// consumer playback URL is exposed until bytes exist — deleting the stored
    /// file or this reference removes playback again.
    /// </summary>
    public void AttachVideo(string storageKey)
    {
        if (Status == LibraryContentStatus.Archived)
            throw new DomainException("Archived library content cannot be modified.");

        if (Type != LibraryContentType.Video)
            throw new DomainException("Only video content can have a video file.");

        string normalized = NormalizeVideoStorageKey(storageKey)
            ?? throw new DomainException("A stored video reference is required.");

        VideoStorageKey = normalized;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Detaches the stored video reference. The physical object is deleted by the
    /// caller only after this change is persisted. Legacy <see cref="VideoUrl"/>
    /// metadata is retained.
    /// </summary>
    public void RemoveVideo()
    {
        if (Status == LibraryContentStatus.Archived)
            throw new DomainException("Archived library content cannot be modified.");
        if (Type != LibraryContentType.Video)
            throw new DomainException("Only video content can have a video file.");

        VideoStorageKey = null;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Publish()
    {
        if (Status == LibraryContentStatus.Archived)
            throw new DomainException("Archived library content cannot be published.");

        Status = LibraryContentStatus.Published;
        PublishedOnUtc ??= DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Archive()
    {
        if (Status == LibraryContentStatus.Archived)
            return;

        Status = LibraryContentStatus.Archived;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Restores archived content to <see cref="LibraryContentStatus.Draft"/>
    /// (locked decision #24). Only archived content can be restored; every
    /// other status is a lifecycle violation. Restored content then follows the
    /// normal draft rules — update and validation apply before it can be
    /// published again.
    /// </summary>
    public void Restore()
    {
        if (Status != LibraryContentStatus.Archived)
            throw new DomainException("Only archived library content can be restored.");

        Status = LibraryContentStatus.Draft;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void IncrementViews() => ViewCount = checked(ViewCount + 1);
    public void IncrementLikes() => LikesCount = checked(LikesCount + 1);
    public void DecrementLikes() => LikesCount = Math.Max(0, LikesCount - 1);

    /// <summary>
    /// Applies the type-specific invariants. Video content requires a positive
    /// duration and no article sections; an external <paramref name="videoUrl"/>
    /// is optional legacy metadata (validated when supplied) and the stored
    /// <paramref name="videoStorageKey"/> is optional until bytes are uploaded.
    /// Requiring an external URL would contradict the approved rule that media is
    /// created by uploading bytes into application storage.
    /// </summary>
    private void ApplyTypeRules(
        LibraryContentType type,
        string? videoUrl,
        string? videoStorageKey,
        int? durationSeconds,
        IReadOnlyList<LibraryContentSectionDraft> sections)
    {
        string? normalizedKey = NormalizeVideoStorageKey(videoStorageKey);

        if (type == LibraryContentType.Video)
        {
            if (durationSeconds is null || durationSeconds <= 0)
                throw new DomainException("Video content requires a positive duration in seconds.");
            if (sections is { Count: > 0 })
                throw new DomainException("Video content cannot have article sections.");

            VideoUrl = NormalizeVideoUrl(videoUrl);
            VideoStorageKey = normalizedKey;
            DurationSeconds = durationSeconds;
            ReplaceSections([]);
            return;
        }

        if (!string.IsNullOrWhiteSpace(videoUrl))
            throw new DomainException("Article content cannot have a video URL.");
        if (normalizedKey is not null)
            throw new DomainException("Article content cannot have a video file.");
        if (durationSeconds is not null)
            throw new DomainException("Article content cannot have a duration.");
        if (sections is null || sections.Count == 0)
            throw new DomainException("Article content requires at least one section.");

        VideoUrl = null;
        VideoStorageKey = null;
        DurationSeconds = null;
        ReplaceSections(sections);
    }

    private static string? NormalizeVideoUrl(string? videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
            return null;

        string normalized = videoUrl.Trim();
        if (normalized.Length > MaximumVideoUrlLength)
            throw new DomainException($"Video URL cannot exceed {MaximumVideoUrlLength} characters.");
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new DomainException("Video URL must be an absolute http(s) URL.");

        return normalized;
    }

    /// <summary>
    /// The stored reference is always produced by the storage layer, and it must
    /// keep pointing inside the private storage area: no traversal, no rooted
    /// path and no scheme separator can be accepted.
    /// </summary>
    private static string? NormalizeVideoStorageKey(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            return null;

        string normalized = storageKey.Trim();
        if (normalized.Length > MaximumVideoStorageKeyLength ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.StartsWith('/'))
        {
            throw new DomainException("Stored video reference is invalid.");
        }

        return normalized;
    }

    private void ReplaceSections(IReadOnlyList<LibraryContentSectionDraft> sections)
    {
        if (sections.Count > MaximumSectionCount)
            throw new DomainException($"Article content supports at most {MaximumSectionCount} sections.");
        if (sections.Select(section => section.DisplayOrder).Distinct().Count() != sections.Count)
            throw new DomainException("Section display orders must be unique.");

        _sections.Clear();
        foreach (LibraryContentSectionDraft draft in sections.OrderBy(section => section.DisplayOrder))
        {
            _sections.Add(LibraryContentSection.Create(
                draft.DisplayOrder,
                draft.ArabicTitle,
                draft.EnglishTitle,
                draft.ArabicBullets,
                draft.EnglishBullets));
        }
    }

    private static string RequiredText(string? value, string name, int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            throw new DomainException($"{name} is required and must not exceed {maximumLength} characters.");
        return normalized;
    }
}

public sealed record LibraryContentSectionDraft(
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    IReadOnlyList<string> ArabicBullets,
    IReadOnlyList<string> EnglishBullets);
