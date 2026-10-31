using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Cms.Domain.Settings;

/// <summary>
/// The single editable site-settings row: the authored navbar links and the
/// authored SEO defaults, both bilingual (Arabic + English).
/// <para>
/// The row is created once and then updated in place. Its identity is fixed
/// (<see cref="SiteSettingsId.Singleton"/>), so a concurrent first write can
/// never create a second row. There is no draft/publish workflow, no
/// generated copy and no translated copy: only what an administrator
/// authored is stored.
/// </para>
/// <para>
/// The navbar links are stored as one ordered JSON payload (the same shape
/// the family-home items use). The authored array order is the navbar order.
/// </para>
/// </summary>
public sealed class SiteSettings : AggregateRoot<SiteSettingsId>
{
    public const int MaximumLinkCount = 20;
    public const int MaximumLinkKeyLength = 60;
    public const int MaximumLinkLabelLength = 120;
    public const int MaximumLinkUrlLength = 500;
    public const int MaximumSeoTitleLength = 200;
    public const int MaximumSeoDescriptionLength = 500;
    public const int MaximumNavbarLinksJsonLength = 20000;

    private SiteSettings()
    {
    }

    private SiteSettings(
        SiteSettingsId id,
        string navbarLinksJson,
        string? arabicSeoTitle,
        string? englishSeoTitle,
        string? arabicSeoDescription,
        string? englishSeoDescription,
        DateTime createdOnUtc)
        : base(id)
    {
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Site settings timestamps must be UTC.");

        NavbarLinksJson = NormalizeLinks(navbarLinksJson);
        ArabicSeoTitle = NormalizeOptional(
            arabicSeoTitle,
            MaximumSeoTitleLength,
            "Arabic SEO title");
        EnglishSeoTitle = NormalizeOptional(
            englishSeoTitle,
            MaximumSeoTitleLength,
            "English SEO title");
        ArabicSeoDescription = NormalizeOptional(
            arabicSeoDescription,
            MaximumSeoDescriptionLength,
            "Arabic SEO description");
        EnglishSeoDescription = NormalizeOptional(
            englishSeoDescription,
            MaximumSeoDescriptionLength,
            "English SEO description");
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    /// <summary>The authored navbar links, as one ordered JSON payload.</summary>
    public string NavbarLinksJson { get; private set; } = string.Empty;

    public string? ArabicSeoTitle { get; private set; }

    public string? EnglishSeoTitle { get; private set; }

    public string? ArabicSeoDescription { get; private set; }

    public string? EnglishSeoDescription { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public static SiteSettings Create(
        string navbarLinksJson,
        string? arabicSeoTitle = null,
        string? englishSeoTitle = null,
        string? arabicSeoDescription = null,
        string? englishSeoDescription = null,
        DateTime? createdOnUtc = null) =>
        new(
            SiteSettingsId.Singleton,
            navbarLinksJson,
            arabicSeoTitle,
            englishSeoTitle,
            arabicSeoDescription,
            englishSeoDescription,
            createdOnUtc ?? DateTime.UtcNow);

    /// <summary>
    /// Replaces the whole settings document in place. The row identity never
    /// changes: an update never creates a second row.
    /// </summary>
    public void Update(
        string navbarLinksJson,
        string? arabicSeoTitle = null,
        string? englishSeoTitle = null,
        string? arabicSeoDescription = null,
        string? englishSeoDescription = null)
    {
        // Every input is normalized into a local first: a later failure must
        // not leave an earlier field already mutated on the tracked entity.
        string normalizedLinks = NormalizeLinks(navbarLinksJson);
        string? normalizedArabicTitle = NormalizeOptional(
            arabicSeoTitle,
            MaximumSeoTitleLength,
            "Arabic SEO title");
        string? normalizedEnglishTitle = NormalizeOptional(
            englishSeoTitle,
            MaximumSeoTitleLength,
            "English SEO title");
        string? normalizedArabicDescription = NormalizeOptional(
            arabicSeoDescription,
            MaximumSeoDescriptionLength,
            "Arabic SEO description");
        string? normalizedEnglishDescription = NormalizeOptional(
            englishSeoDescription,
            MaximumSeoDescriptionLength,
            "English SEO description");

        NavbarLinksJson = normalizedLinks;
        ArabicSeoTitle = normalizedArabicTitle;
        EnglishSeoTitle = normalizedEnglishTitle;
        ArabicSeoDescription = normalizedArabicDescription;
        EnglishSeoDescription = normalizedEnglishDescription;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string NormalizeLinks(string navbarLinksJson)
    {
        string normalized = (navbarLinksJson ?? string.Empty).Trim();

        if (normalized.Length > MaximumNavbarLinksJsonLength)
        {
            throw new DomainException(
                $"Navbar links cannot exceed {MaximumNavbarLinksJsonLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
            throw new DomainException($"{fieldName} cannot exceed {maximumLength} characters.");

        return normalized;
    }
}
