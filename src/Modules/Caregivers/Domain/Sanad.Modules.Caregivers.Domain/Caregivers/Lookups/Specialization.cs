using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class Specialization : AggregateRoot<SpecializationId>
{
    public const int MaximumNameLength = 150;

    private Specialization()
    {
    }

    private Specialization(
        SpecializationId id,
        string arabicName,
        string englishName,
        CaregiverType caregiverType,
        DateTime createdOnUtc)
        : base(id)
    {
        ArabicName = arabicName;
        EnglishName = englishName;
        CaregiverType = caregiverType;
        IsActive = true;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string ArabicName { get; private set; } =
        string.Empty;

    public string EnglishName { get; private set; } =
        string.Empty;

    public CaregiverType CaregiverType { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static Specialization Create(
        string arabicName,
        string englishName,
        CaregiverType caregiverType)
    {
        string normalizedArabicName =
            NormalizeName(
                arabicName,
                "Arabic");

        string normalizedEnglishName =
            NormalizeName(
                englishName,
                "English");

        if (!Enum.IsDefined(caregiverType))
        {
            throw new DomainException(
                "Caregiver type is invalid.");
        }

        DateTime createdOnUtc =
            DateTime.UtcNow;

        return new Specialization(
            SpecializationId.New(),
            normalizedArabicName,
            normalizedEnglishName,
            caregiverType,
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
                $"{language} specialization name is required.");
        }

        string normalizedName =
            name.Trim();

        if (normalizedName.Length >
            MaximumNameLength)
        {
            throw new DomainException(
                $"{language} specialization name cannot exceed " +
                $"{MaximumNameLength} characters.");
        }

        return normalizedName;
    }
}