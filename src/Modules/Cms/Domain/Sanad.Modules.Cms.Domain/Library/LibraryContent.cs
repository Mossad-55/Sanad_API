using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Library;

public sealed class LibraryContent : AggregateRoot<LibraryContentId>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumSummaryLength = 1000;
    public const int MaximumVideoUrlLength = 500;
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
        ApplyTypeRules(type, videoUrl, durationSeconds, sections);
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
        ApplyTypeRules(Type, videoUrl, durationSeconds, sections);
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

    public void IncrementViews() => ViewCount = checked(ViewCount + 1);
    public void IncrementLikes() => LikesCount = checked(LikesCount + 1);
    public void DecrementLikes() => LikesCount = Math.Max(0, LikesCount - 1);

    private void ApplyTypeRules(
        LibraryContentType type,
        string? videoUrl,
        int? durationSeconds,
        IReadOnlyList<LibraryContentSectionDraft> sections)
    {
        if (type == LibraryContentType.Video)
        {
            if (string.IsNullOrWhiteSpace(videoUrl))
                throw new DomainException("Video content requires a video URL.");
            string normalizedUrl = videoUrl.Trim();
            if (normalizedUrl.Length > MaximumVideoUrlLength)
                throw new DomainException($"Video URL cannot exceed {MaximumVideoUrlLength} characters.");
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new DomainException("Video URL must be an absolute http(s) URL.");
            if (durationSeconds is null || durationSeconds <= 0)
                throw new DomainException("Video content requires a positive duration in seconds.");
            if (sections is { Count: > 0 })
                throw new DomainException("Video content cannot have article sections.");

            VideoUrl = normalizedUrl;
            DurationSeconds = durationSeconds;
            ReplaceSections([]);
            return;
        }

        if (!string.IsNullOrWhiteSpace(videoUrl))
            throw new DomainException("Article content cannot have a video URL.");
        if (durationSeconds is not null)
            throw new DomainException("Article content cannot have a duration.");
        if (sections is null || sections.Count == 0)
            throw new DomainException("Article content requires at least one section.");

        VideoUrl = null;
        DurationSeconds = null;
        ReplaceSections(sections);
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
