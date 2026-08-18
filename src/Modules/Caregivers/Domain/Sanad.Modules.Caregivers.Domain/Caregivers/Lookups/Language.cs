using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class Language : Entity<LanguageId>
{
    public const int MaximumNameLength = 100;

    private Language()
    {
    }

    private Language(
        LanguageId id,
        string code,
        string arabicName,
        string englishName,
        DateTime createdOnUtc)
        : base(id)
    {
        Code = code;
        ArabicName = arabicName;
        EnglishName = englishName;
        IsActive = true;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string Code { get; private set; } = string.Empty;

    public string ArabicName { get; private set; } = string.Empty;

    public string EnglishName { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static Language Create(
        string code,
        string arabicName,
        string englishName)
    {
        string normalizedCode =
            NormalizeCode(code);

        string normalizedArabicName =
            NormalizeName(
                arabicName,
                "Arabic");

        string normalizedEnglishName =
            NormalizeName(
                englishName,
                "English");

        DateTime createdOnUtc = DateTime.UtcNow;

        return new Language(
            LanguageId.New(),
            normalizedCode,
            normalizedArabicName,
            normalizedEnglishName,
            createdOnUtc);
    }

    public void UpdateNames(
        string arabicName,
        string englishName)
    {
        ArabicName = NormalizeName(
            arabicName,
            "Arabic");

        EnglishName = NormalizeName(
            englishName,
            "English");

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

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException(
                "Language code is required.");
        }

        string normalizedCode =
            code.Trim().ToLowerInvariant();

        bool hasInvalidLength =
            normalizedCode.Length is < 2 or > 3;

        bool hasInvalidCharacters =
            normalizedCode.Any(
                character =>
                    character is < 'a' or > 'z');

        if (hasInvalidLength ||
            hasInvalidCharacters)
        {
            throw new DomainException(
                "Language code must contain two or three letters.");
        }

        return normalizedCode;
    }

    private static string NormalizeName(
        string name,
        string language)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException(
                $"{language} language name is required.");
        }

        string normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            throw new DomainException(
                $"{language} language name cannot exceed " +
                $"{MaximumNameLength} characters.");
        }

        return normalizedName;
    }
}