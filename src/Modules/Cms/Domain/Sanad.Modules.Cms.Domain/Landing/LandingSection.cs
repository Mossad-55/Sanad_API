using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Landing;

/// <summary>
/// Phase 1 read-model entity: a CMS-managed landing section row.
/// Draft/Published lifecycle + <c>IsVisible</c> + <c>DisplayOrder</c>.
/// Drafts may be partial; <see cref="Publish"/> requires bilingual titles.
/// Per-type required assets are validated by Phase 2 publish validation.
/// </summary>
public sealed class LandingSection : AggregateRoot<LandingSectionId>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumBodyLength = 4000;
    public const int MaximumContentJsonLength = 8000;
    public const int MaximumImagePathLength = 500;

    private LandingSection()
    {
    }

    private LandingSection(
        LandingSectionId id,
        LandingSectionType type,
        string arabicTitle,
        string englishTitle,
        string? arabicBody,
        string? englishBody,
        string? contentJson,
        string? imagePath,
        int displayOrder,
        bool isVisible,
        LandingSectionId? sourceId,
        DateTime createdOnUtc)
        : base(id)
    {
        if (!Enum.IsDefined(type))
            throw new DomainException("Landing section type is invalid.");
        if (type == LandingSectionType.Stats)
            throw new DomainException("The virtual stats section cannot be stored.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Landing section timestamps must be UTC.");
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        Type = type;
        ArabicTitle = NormalizeOptionalText(arabicTitle, nameof(arabicTitle), MaximumTitleLength);
        EnglishTitle = NormalizeOptionalText(englishTitle, nameof(englishTitle), MaximumTitleLength);
        ArabicBody = NormalizeOptionalText(arabicBody, nameof(arabicBody), MaximumBodyLength);
        EnglishBody = NormalizeOptionalText(englishBody, nameof(englishBody), MaximumBodyLength);
        ContentJson = NormalizeOptionalText(contentJson, nameof(contentJson), MaximumContentJsonLength);
        ImagePath = NormalizeOptionalText(imagePath, nameof(imagePath), MaximumImagePathLength);
        DisplayOrder = displayOrder;
        IsVisible = isVisible;
        SourceId = sourceId;
        Status = LandingSectionStatus.Draft;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public LandingSectionType Type { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string? ArabicBody { get; private set; }
    public string? EnglishBody { get; private set; }
    public string? ContentJson { get; private set; }
    public string? ImagePath { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsVisible { get; private set; }
    public LandingSectionId? SourceId { get; private set; }
    public LandingSectionStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }

    public static LandingSection Create(
        LandingSectionType type,
        string? arabicTitle = null,
        string? englishTitle = null,
        string? arabicBody = null,
        string? englishBody = null,
        string? contentJson = null,
        string? imagePath = null,
        int displayOrder = 0,
        bool isVisible = true,
        LandingSectionId? sourceId = null,
        DateTime? createdOnUtc = null) =>
        new(
            LandingSectionId.New(),
            type,
            arabicTitle ?? string.Empty,
            englishTitle ?? string.Empty,
            arabicBody,
            englishBody,
            contentJson,
            imagePath,
            displayOrder,
            isVisible,
            sourceId,
            createdOnUtc ?? DateTime.UtcNow);

    public void Publish()
    {
        if (Status == LandingSectionStatus.Published)
            return;

        if (Status == LandingSectionStatus.Archived)
            throw new DomainException("Archived landing sections cannot be published.");

        if (string.IsNullOrWhiteSpace(ArabicTitle) || string.IsNullOrWhiteSpace(EnglishTitle))
            throw new DomainException("Publishing a landing section requires Arabic and English titles.");

        Status = LandingSectionStatus.Published;
        PublishedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Unpublish()
    {
        if (Status == LandingSectionStatus.Draft)
            return;

        if (Status == LandingSectionStatus.Archived)
            throw new DomainException("Archived landing sections cannot be unpublished.");

        Status = LandingSectionStatus.Draft;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Archive()
    {
        if (Status == LandingSectionStatus.Archived)
            return;

        Status = LandingSectionStatus.Archived;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateDraft(
        string? arabicTitle,
        string? englishTitle,
        string? arabicBody,
        string? englishBody,
        string? contentJson,
        string? imagePath,
        int displayOrder,
        bool isVisible)
    {
        if (Status != LandingSectionStatus.Draft)
            throw new DomainException("Only draft landing sections can be modified.");
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        ArabicTitle = NormalizeOptionalText(arabicTitle, nameof(arabicTitle), MaximumTitleLength);
        EnglishTitle = NormalizeOptionalText(englishTitle, nameof(englishTitle), MaximumTitleLength);
        ArabicBody = NormalizeOptionalText(arabicBody, nameof(arabicBody), MaximumBodyLength);
        EnglishBody = NormalizeOptionalText(englishBody, nameof(englishBody), MaximumBodyLength);
        ContentJson = NormalizeOptionalText(contentJson, nameof(contentJson), MaximumContentJsonLength);
        ImagePath = NormalizeOptionalText(imagePath, nameof(imagePath), MaximumImagePathLength);
        DisplayOrder = displayOrder;
        IsVisible = isVisible;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SetVisibility(bool isVisible)
    {
        IsVisible = isVisible;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SetDisplayOrder(int displayOrder)
    {
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        DisplayOrder = displayOrder;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string NormalizeOptionalText(string? value, string fieldName, int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;

        if (normalized.Length > maximumLength)
            throw new DomainException($"{fieldName} cannot exceed {maximumLength} characters.");

        return normalized;
    }
}
