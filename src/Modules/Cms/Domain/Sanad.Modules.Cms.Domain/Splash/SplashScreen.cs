using System.Text.RegularExpressions;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Domain.Splash;

public sealed class SplashScreen : AggregateRoot<SplashScreenId>
{
    public const int MaximumInternalNameLength = 100;
    public const int MaximumTitleLength = 150;
    public const int MaximumDescriptionLength = 500;
    public const int MaximumButtonTextLength = 50;
    public const int MaximumImagePathLength = 500;
    public const int BackgroundColorLength = 7;

    private static readonly Regex BackgroundColorPattern =
        new(@"\A#[0-9A-Fa-f]{6}\z", RegexOptions.Compiled);

    private SplashScreen()
    {
    }

    private SplashScreen(
        SplashScreenId id,
        string internalName,
        SplashAudience audience,
        string arabicTitle,
        string englishTitle,
        string arabicDescription,
        string englishDescription,
        string arabicButtonText,
        string englishButtonText,
        string imagePath,
        string backgroundColor,
        int displayOrder,
        DateTime createdOnUtc)
        : base(id)
    {
        InternalName = internalName;
        Audience = audience;
        ArabicTitle = arabicTitle;
        EnglishTitle = englishTitle;
        ArabicDescription = arabicDescription;
        EnglishDescription = englishDescription;
        ArabicButtonText = arabicButtonText;
        EnglishButtonText = englishButtonText;
        ImagePath = imagePath;
        BackgroundColor = backgroundColor;
        DisplayOrder = displayOrder;
        Status = SplashPublicationStatus.Draft;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string InternalName { get; private set; } = string.Empty;
    public SplashAudience Audience { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string ArabicDescription { get; private set; } = string.Empty;
    public string EnglishDescription { get; private set; } = string.Empty;
    public string ArabicButtonText { get; private set; } = string.Empty;
    public string EnglishButtonText { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public string BackgroundColor { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public SplashPublicationStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static SplashScreen Create(
        string internalName,
        SplashAudience audience,
        string arabicTitle,
        string englishTitle,
        string arabicDescription,
        string englishDescription,
        string arabicButtonText,
        string englishButtonText,
        string imagePath,
        string backgroundColor,
        int displayOrder)
    {
        string normalizedInternalName =
            NormalizeRequiredText(
                internalName,
                "Internal name",
                MaximumInternalNameLength);

        if (!Enum.IsDefined(audience))
        {
            throw new DomainException(
                "Splash audience is invalid.");
        }

        string normalizedArabicTitle =
            NormalizeRequiredText(
                arabicTitle,
                "Arabic title",
                MaximumTitleLength);

        string normalizedEnglishTitle =
            NormalizeRequiredText(
                englishTitle,
                "English title",
                MaximumTitleLength);

        string normalizedArabicDescription =
            NormalizeRequiredText(
                arabicDescription,
                "Arabic description",
                MaximumDescriptionLength);

        string normalizedEnglishDescription =
            NormalizeRequiredText(
                englishDescription,
                "English description",
                MaximumDescriptionLength);

        string normalizedArabicButtonText =
            NormalizeRequiredText(
                arabicButtonText,
                "Arabic button text",
                MaximumButtonTextLength);

        string normalizedEnglishButtonText =
            NormalizeRequiredText(
                englishButtonText,
                "English button text",
                MaximumButtonTextLength);

        string normalizedImagePath =
            NormalizeRequiredText(
                imagePath,
                "Image path",
                MaximumImagePathLength);

        string normalizedBackgroundColor =
            NormalizeBackgroundColor(
                backgroundColor);

        if (displayOrder < 0)
        {
            throw new DomainException(
                "Display order cannot be negative.");
        }

        return new SplashScreen(
            SplashScreenId.New(),
            normalizedInternalName,
            audience,
            normalizedArabicTitle,
            normalizedEnglishTitle,
            normalizedArabicDescription,
            normalizedEnglishDescription,
            normalizedArabicButtonText,
            normalizedEnglishButtonText,
            normalizedImagePath,
            normalizedBackgroundColor,
            displayOrder,
            DateTime.UtcNow);
    }

    public void UpdateContent(
        string arabicTitle,
        string englishTitle,
        string arabicDescription,
        string englishDescription,
        string arabicButtonText,
        string englishButtonText,
        string imagePath,
        string backgroundColor,
        int displayOrder)
    {
        string normalizedArabicTitle =
            NormalizeRequiredText(
                arabicTitle,
                "Arabic title",
                MaximumTitleLength);

        string normalizedEnglishTitle =
            NormalizeRequiredText(
                englishTitle,
                "English title",
                MaximumTitleLength);

        string normalizedArabicDescription =
            NormalizeRequiredText(
                arabicDescription,
                "Arabic description",
                MaximumDescriptionLength);

        string normalizedEnglishDescription =
            NormalizeRequiredText(
                englishDescription,
                "English description",
                MaximumDescriptionLength);

        string normalizedArabicButtonText =
            NormalizeRequiredText(
                arabicButtonText,
                "Arabic button text",
                MaximumButtonTextLength);

        string normalizedEnglishButtonText =
            NormalizeRequiredText(
                englishButtonText,
                "English button text",
                MaximumButtonTextLength);

        string normalizedImagePath =
            NormalizeRequiredText(
                imagePath,
                "Image path",
                MaximumImagePathLength);

        string normalizedBackgroundColor =
            NormalizeBackgroundColor(
                backgroundColor);

        if (displayOrder < 0)
        {
            throw new DomainException(
                "Display order cannot be negative.");
        }

        ArabicTitle = normalizedArabicTitle;
        EnglishTitle = normalizedEnglishTitle;
        ArabicDescription = normalizedArabicDescription;
        EnglishDescription = normalizedEnglishDescription;
        ArabicButtonText = normalizedArabicButtonText;
        EnglishButtonText = normalizedEnglishButtonText;
        ImagePath = normalizedImagePath;
        BackgroundColor = normalizedBackgroundColor;
        DisplayOrder = displayOrder;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Publish()
    {
        if (Status == SplashPublicationStatus.Published)
        {
            return;
        }

        Status = SplashPublicationStatus.Published;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Unpublish()
    {
        if (Status == SplashPublicationStatus.Draft)
        {
            return;
        }

        Status = SplashPublicationStatus.Draft;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static string NormalizeRequiredText(
        string value,
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

    private static string NormalizeBackgroundColor(
        string backgroundColor)
    {
        if (string.IsNullOrWhiteSpace(backgroundColor))
        {
            throw new DomainException(
                "Background color is required.");
        }

        string normalized = backgroundColor.Trim();

        if (!BackgroundColorPattern.IsMatch(normalized))
        {
            throw new DomainException(
                "Background color must be a #RRGGBB hex value.");
        }

        return normalized.ToUpperInvariant();
    }
}