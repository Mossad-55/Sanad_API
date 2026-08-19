using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class ProfessionalTitle :
    AggregateRoot<ProfessionalTitleId>
{
    public const int MaximumNameLength = 150;

    private ProfessionalTitle()
    {
    }

    private ProfessionalTitle(
        ProfessionalTitleId id,
        string arabicName,
        string englishName,
        DateTime createdOnUtc)
        : base(id)
    {
        ArabicName = arabicName;
        EnglishName = englishName;
        IsActive = true;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string ArabicName { get; private set; } =
        string.Empty;

    public string EnglishName { get; private set; } =
        string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static ProfessionalTitle Create(
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

        DateTime createdOnUtc =
            DateTime.UtcNow;

        return new ProfessionalTitle(
            ProfessionalTitleId.New(),
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
                $"{language} professional title name is required.");
        }

        string normalizedName =
            name.Trim();

        if (normalizedName.Length >
            MaximumNameLength)
        {
            throw new DomainException(
                $"{language} professional title name cannot exceed " +
                $"{MaximumNameLength} characters.");
        }

        return normalizedName;
    }
}