using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class Service : AggregateRoot<ServiceId>
{
    public const int MaximumNameLength = 150;
    public const int MaximumIconPathLength = 500;

    private Service()
    {
    }

    private Service(
        ServiceId id,
        string arabicName,
        string englishName,
        string iconPath,
        CaregiverType caregiverType,
        bool isActive,
        DateTime createdOnUtc)
        : base(id)
    {
        ArabicName = arabicName;
        EnglishName = englishName;
        IconPath = iconPath;
        CaregiverType = caregiverType;
        IsActive = isActive;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string ArabicName { get; private set; } = string.Empty;
    public string EnglishName { get; private set; } = string.Empty;
    public CaregiverType CaregiverType { get; private set; }
    public bool IsActive { get; private set; }
    public string IconPath { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static Service Create(
        string arabicName,
        string englishName,
        string iconPath,
        CaregiverType caregiverType,
        bool isActive)
    {
        string normalizedArabicName = NormalizeName(
            arabicName,
            "Arabic");

        string normalizedEnglishName = NormalizeName(
            englishName,
            "English");

        string normalizedIconPath = NormalizeIconPath(iconPath);

        if (!Enum.IsDefined(caregiverType))
        {
            throw new DomainException(
                "Caregiver type is invalid.");
        }

        DateTime createdOnUtc = DateTime.UtcNow;

    return new Service(
        ServiceId.New(),
        normalizedArabicName,
        normalizedEnglishName,
        normalizedIconPath,
        caregiverType,
        isActive,
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

    public void UpdateIcon(
        string iconPath)
    {
        string normalizedIconPath =
            NormalizeIconPath(iconPath);

        if (IconPath == normalizedIconPath)
        {
            return;
        }

        IconPath = normalizedIconPath;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string NormalizeName(
        string name,
        string language)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(
                $"{language} service name is required.");
        }

        string normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            throw new DomainException(
                $"{language} service name cannot exceed " +
                $"{MaximumNameLength} characters.");
        }

        return normalizedName;
    }

    private static string NormalizeIconPath(
        string iconPath)
    {
        if (string.IsNullOrWhiteSpace(
            iconPath))
        {
            throw new DomainException(
                "Service icon is required.");
        }

        string normalizedIconPath =
            iconPath.Trim();

        if (normalizedIconPath.Length >
            MaximumIconPathLength)
        {
            throw new DomainException(
                $"Service icon path cannot exceed " +
                $"{MaximumIconPathLength} characters.");
        }

        return normalizedIconPath;
    }
}