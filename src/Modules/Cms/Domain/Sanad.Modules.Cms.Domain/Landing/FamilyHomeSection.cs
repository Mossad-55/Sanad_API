using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Domain.Landing;

public sealed class FamilyHomeSection : AggregateRoot<FamilyHomeSectionId>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumBodyLength = 4000;
    public const int MaximumKeyLength = 500;
    public const int MaximumItemsJsonLength = 40000;

    private FamilyHomeSection()
    {
    }

    private FamilyHomeSection(
        FamilyHomeSectionId id,
        FamilyHomeSectionKind kind,
        string? itemsJson,
        int displayOrder,
        bool isVisible,
        FamilyHomeSectionId? sourceId,
        DateTime createdOnUtc)
        : base(id)
    {
        if (!Enum.IsDefined(kind))
            throw new DomainException("Family home section kind is invalid.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Family home section timestamps must be UTC.");
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        Kind = kind;
        ItemsJson = NormalizeOptionalText(itemsJson, nameof(itemsJson), MaximumItemsJsonLength);
        DisplayOrder = displayOrder;
        IsVisible = isVisible;
        SourceId = sourceId;
        Status = FamilyHomeSectionStatus.Draft;
        Version = 1;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public FamilyHomeSectionKind Kind { get; private set; }
    public string? ItemsJson { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsVisible { get; private set; }
    public FamilyHomeSectionId? SourceId { get; private set; }
    public FamilyHomeSectionStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }

    public static FamilyHomeSection Create(
        FamilyHomeSectionKind kind,
        string? itemsJson = null,
        int displayOrder = 0,
        bool isVisible = true,
        FamilyHomeSectionId? sourceId = null,
        DateTime? createdOnUtc = null) =>
        new(
            FamilyHomeSectionId.New(),
            kind,
            itemsJson,
            displayOrder,
            isVisible,
            sourceId,
            createdOnUtc ?? DateTime.UtcNow);

    public void Publish(int expectedVersion)
    {
        if (Version != expectedVersion)
            throw new DomainException("Stale family home section version.");
        if (Status == FamilyHomeSectionStatus.Published)
            return;
        if (Status == FamilyHomeSectionStatus.Archived)
            throw new DomainException("Archived family home sections cannot be published.");

        Status = FamilyHomeSectionStatus.Published;
        PublishedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
        Version++;
    }

    public void Archive()
    {
        if (Status == FamilyHomeSectionStatus.Archived)
            return;

        Status = FamilyHomeSectionStatus.Archived;
        UpdatedOnUtc = DateTime.UtcNow;
        Version++;
    }

    public void UpdateDraft(
        string? itemsJson,
        int displayOrder,
        bool isVisible,
        int expectedVersion)
    {
        if (Status != FamilyHomeSectionStatus.Draft)
            throw new DomainException("Only draft family home sections can be modified.");
        if (Version != expectedVersion)
            throw new DomainException("Stale family home section version.");
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        ItemsJson = NormalizeOptionalText(itemsJson, nameof(itemsJson), MaximumItemsJsonLength);
        DisplayOrder = displayOrder;
        IsVisible = isVisible;
        UpdatedOnUtc = DateTime.UtcNow;
        Version++;
    }

    public void SetVisibility(bool isVisible, int expectedVersion)
    {
        if (Version != expectedVersion)
            throw new DomainException("Stale family home section version.");

        IsVisible = isVisible;
        UpdatedOnUtc = DateTime.UtcNow;
        Version++;
    }

    public void SetDisplayOrder(int displayOrder)
    {
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        DisplayOrder = displayOrder;
        UpdatedOnUtc = DateTime.UtcNow;
        Version++;
    }

    private static string NormalizeOptionalText(string? value, string fieldName, int maximumLength)
    {
        if (value is null)
            return string.Empty;

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{fieldName} cannot exceed {maximumLength} characters.");

        return normalized;
    }
}