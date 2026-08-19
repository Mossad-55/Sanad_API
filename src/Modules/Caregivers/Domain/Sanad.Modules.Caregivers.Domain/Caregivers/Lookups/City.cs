using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class City : AggregateRoot<CityId>
{
    public const int MaximumNameLength = 150;

    private City()
    {
    }

    private City(
        CityId id,
        GovernorateId governorateId,
        string arabicName,
        string englishName,
        DateTime createdOnUtc)
        : base(id)
    {
        GovernorateId = governorateId;
        ArabicName = arabicName;
        EnglishName = englishName;
        IsActive = true;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public GovernorateId GovernorateId { get; private set; }

    public string ArabicName { get; private set; } = string.Empty;

    public string EnglishName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static City Create(
        GovernorateId governorateId,
        string arabicName,
        string englishName)
    {
        if (governorateId == GovernorateId.Empty)
        {
            throw new DomainException(
                "Governorate ID is required.");
        }

        string normalizedArabicName =
            NormalizeName(
                arabicName,
                "Arabic");

        string normalizedEnglishName =
            NormalizeName(
                englishName,
                "English");

        DateTime createdOnUtc = DateTime.UtcNow;

        return new City(
            CityId.New(),
            governorateId,
            normalizedArabicName,
            normalizedEnglishName,
            createdOnUtc);
    }

    public void UpdateNames(
        string arabicName,
        string englishName)
    {
        string normalizedArabicName =
            NormalizeName(
                arabicName,
                "Arabic");

        string normalizedEnglishName =
            NormalizeName(
                englishName,
                "English");

        ArabicName = normalizedArabicName;
        EnglishName = normalizedEnglishName;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string NormalizeName(
        string name,
        string language)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(
                $"{language} city name is required.");
        }

        string normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            throw new DomainException(
                $"{language} city name cannot exceed " +
                $"{MaximumNameLength} characters.");
        }

        return normalizedName;
    }
}