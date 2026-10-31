using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Community.Domain.Categories;

public sealed class CommunityCategory : AggregateRoot<CommunityCategoryId>
{
    public const int MaximumNameLength = 200;

    private CommunityCategory()
    {
    }

    private CommunityCategory(
        CommunityCategoryId id,
        string arabicName,
        string englishName,
        bool isActive,
        DateTime createdOnUtc)
        : base(id)
    {
        ArabicName = RequiredName(arabicName, nameof(arabicName));
        EnglishName = RequiredName(englishName, nameof(englishName));
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community category timestamps must be UTC.");

        IsActive = isActive;
        CreatedOnUtc = createdOnUtc;
    }

    public string ArabicName { get; private set; } = string.Empty;
    public string EnglishName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public static CommunityCategory Create(
        string arabicName,
        string englishName,
        bool isActive,
        DateTime createdOnUtc) =>
        new(CommunityCategoryId.New(), arabicName, englishName, isActive, createdOnUtc);

    public void Update(string arabicName, string englishName, bool isActive, DateTime updatedOnUtc)
    {
        ArabicName = RequiredName(arabicName, nameof(arabicName));
        EnglishName = RequiredName(englishName, nameof(englishName));
        if (updatedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community category timestamps must be UTC.");

        IsActive = isActive;
        UpdatedOnUtc = updatedOnUtc;
    }

    private static string RequiredName(string value, string name)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > MaximumNameLength)
            throw new DomainException($"{name} is required and must not exceed {MaximumNameLength} characters.");
        return normalized;
    }
}
