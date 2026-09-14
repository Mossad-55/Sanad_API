using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// One ordered, bilingual section of a legal document version.
/// LegalSection is owned by the <see cref="LegalDocument"/> aggregate.
/// </summary>
public sealed class LegalSection : Entity<Guid>
{
    public const int MaximumTitleLength = 150;
    public const int MaximumDescriptionLength = 4000;
    public const int MaximumBulletCount = 20;
    public const int MaximumBulletLength = 500;

    private LegalSection()
    {
    }

    internal LegalSection(
        Guid id,
        LegalSectionType sectionType,
        int displayOrder,
        string arabicTitle,
        string englishTitle,
        string arabicDescription,
        string englishDescription,
        IReadOnlyList<string> arabicBullets,
        IReadOnlyList<string> englishBullets)
        : base(id)
    {
        SectionType = sectionType;
        DisplayOrder = displayOrder;
        ArabicTitle = arabicTitle;
        EnglishTitle = englishTitle;
        ArabicDescription = arabicDescription;
        EnglishDescription = englishDescription;
        ArabicBullets = arabicBullets;
        EnglishBullets = englishBullets;
    }

    public LegalSectionType SectionType { get; private set; }
    public int DisplayOrder { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string ArabicDescription { get; private set; } = string.Empty;
    public string EnglishDescription { get; private set; } = string.Empty;
    public IReadOnlyList<string> ArabicBullets { get; private set; } = [];
    public IReadOnlyList<string> EnglishBullets { get; private set; } = [];

    internal static LegalSection Create(
        LegalSectionType sectionType,
        int displayOrder,
        string? arabicTitle,
        string? englishTitle,
        string? arabicDescription,
        string? englishDescription,
        IReadOnlyList<string>? arabicBullets,
        IReadOnlyList<string>? englishBullets)
    {
        if (displayOrder < 1)
        {
            throw new DomainException(
                "Section display order must be a positive number.");
        }

        return new LegalSection(
            Guid.CreateVersion7(),
            sectionType,
            displayOrder,
            NormalizeRequiredText(
                arabicTitle,
                "Arabic section title",
                MaximumTitleLength),
            NormalizeRequiredText(
                englishTitle,
                "English section title",
                MaximumTitleLength),
            NormalizeRequiredText(
                arabicDescription,
                "Arabic section description",
                MaximumDescriptionLength),
            NormalizeRequiredText(
                englishDescription,
                "English section description",
                MaximumDescriptionLength),
            NormalizeBullets(
                arabicBullets,
                "Arabic"),
            NormalizeBullets(
                englishBullets,
                "English"));
    }

    private static IReadOnlyList<string> NormalizeBullets(
        IReadOnlyList<string>? bullets,
        string languageName)
    {
        if (bullets is null || bullets.Count == 0)
        {
            return [];
        }

        if (bullets.Count > MaximumBulletCount)
        {
            throw new DomainException(
                $"{languageName} bullets cannot exceed " +
                $"{MaximumBulletCount} items.");
        }

        List<string> normalized = [];

        foreach (string bullet in bullets)
        {
            normalized.Add(
                NormalizeRequiredText(
                    bullet,
                    $"{languageName} bullet",
                    MaximumBulletLength));
        }

        return normalized;
    }

    private static string NormalizeRequiredText(
        string? value,
        string fieldName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException(
                $"{fieldName} is required.");
        }

        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new DomainException(
                $"{fieldName} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
