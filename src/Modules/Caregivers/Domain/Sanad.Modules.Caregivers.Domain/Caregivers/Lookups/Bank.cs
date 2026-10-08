using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

public sealed class Bank : AggregateRoot<BankId>
{
    public const int MaximumNameLength = 100;
    public const int MinimumCodeLength = 3;
    public const int MaximumCodeLength = 11;

    private Bank()
    {
    }

    private Bank(
        BankId id,
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

    public static Bank Create(
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

        return new Bank(
            BankId.New(),
            normalizedCode,
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

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException(
                "Bank code is required.");
        }

        string normalizedCode =
            code.Trim().ToUpperInvariant();

        bool hasInvalidLength =
            normalizedCode.Length is < MinimumCodeLength or > MaximumCodeLength;

        bool hasInvalidCharacters =
            normalizedCode.Any(
                character =>
                    character is (< 'A' or > 'Z') and (< '0' or > '9'));

        if (hasInvalidLength ||
            hasInvalidCharacters)
        {
            throw new DomainException(
                "Bank code must contain three to eleven uppercase letters or digits.");
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
                $"{language} bank name is required.");
        }

        string normalizedName = name.Trim();

        if (normalizedName.Length > MaximumNameLength)
        {
            throw new DomainException(
                $"{language} bank name cannot exceed " +
                $"{MaximumNameLength} characters.");
        }

        return normalizedName;
    }
}
