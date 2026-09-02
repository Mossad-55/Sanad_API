using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Assessments;

public sealed class AssessmentTier : AggregateRoot<AssessmentTierId>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumSubtitleLength = 500;
    public const int MaximumButtonTextLength = 50;
    public const int MaximumColorLength = 20;
    public const int MaximumImagePathLength = 500;

    private readonly List<string> _arabicRecommendations = [];
    private readonly List<string> _englishRecommendations = [];

    private AssessmentTier()
    {
    }

    private AssessmentTier(
        AssessmentTierId id,
        int screenOrder,
        string arabicTitle,
        string englishTitle,
        string arabicSubtitle,
        string englishSubtitle,
        string backgroundColor,
        string arabicButtonText,
        string englishButtonText,
        string imagePath,
        int minScore,
        int maxScore,
        bool isActive,
        IEnumerable<string> arabicRecs,
        IEnumerable<string> englishRecs)
        : base(id)
    {
        ScreenOrder = screenOrder;
        ArabicTitle = arabicTitle;
        EnglishTitle = englishTitle;
        ArabicSubtitle = arabicSubtitle;
        EnglishSubtitle = englishSubtitle;
        BackgroundColor = backgroundColor;
        ArabicButtonText = arabicButtonText;
        EnglishButtonText = englishButtonText;
        ImagePath = imagePath;
        MinScore = minScore;
        MaxScore = maxScore;
        IsActive = isActive;

        _arabicRecommendations.AddRange(arabicRecs);
        _englishRecommendations.AddRange(englishRecs);

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public int ScreenOrder { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string ArabicSubtitle { get; private set; } = string.Empty;
    public string EnglishSubtitle { get; private set; } = string.Empty;
    public string BackgroundColor { get; private set; } = string.Empty;
    public string ArabicButtonText { get; private set; } = string.Empty;
    public string EnglishButtonText { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public int MinScore { get; private set; }
    public int MaxScore { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public IReadOnlyList<string> ArabicRecommendations =>
        _arabicRecommendations.AsReadOnly();

    public IReadOnlyList<string> EnglishRecommendations =>
        _englishRecommendations.AsReadOnly();

    public static AssessmentTier Create(
        int screenOrder,
        string arabicTitle,
        string englishTitle,
        string arabicSubtitle,
        string englishSubtitle,
        string backgroundColor,
        string arabicButtonText,
        string englishButtonText,
        string imagePath,
        int minScore,
        int maxScore,
        IEnumerable<string> arabicRecs,
        IEnumerable<string> englishRecs,
        bool isActive = true)
    {
        ValidateInputs(
            screenOrder,
            arabicTitle,
            englishTitle,
            arabicSubtitle,
            englishSubtitle,
            backgroundColor,
            arabicButtonText,
            englishButtonText,
            imagePath,
            minScore,
            maxScore);

        return new AssessmentTier(
            AssessmentTierId.New(),
            screenOrder,
            arabicTitle.Trim(),
            englishTitle.Trim(),
            arabicSubtitle.Trim(),
            englishSubtitle.Trim(),
            backgroundColor.Trim(),
            arabicButtonText.Trim(),
            englishButtonText.Trim(),
            imagePath.Trim(),
            minScore,
            maxScore,
            isActive,
            arabicRecs.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()),
            englishRecs.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));
    }

    public void Update(
        int screenOrder,
        string arabicTitle,
        string englishTitle,
        string arabicSubtitle,
        string englishSubtitle,
        string backgroundColor,
        string arabicButtonText,
        string englishButtonText,
        int minScore,
        int maxScore,
        IEnumerable<string> arabicRecs,
        IEnumerable<string> englishRecs)
    {
        ValidateInputs(
            screenOrder,
            arabicTitle,
            englishTitle,
            arabicSubtitle,
            englishSubtitle,
            backgroundColor,
            arabicButtonText,
            englishButtonText,
            ImagePath,
            minScore,
            maxScore);

        ScreenOrder = screenOrder;
        ArabicTitle = arabicTitle.Trim();
        EnglishTitle = englishTitle.Trim();
        ArabicSubtitle = arabicSubtitle.Trim();
        EnglishSubtitle = englishSubtitle.Trim();
        BackgroundColor = backgroundColor.Trim();
        ArabicButtonText = arabicButtonText.Trim();
        EnglishButtonText = englishButtonText.Trim();
        MinScore = minScore;
        MaxScore = maxScore;

        _arabicRecommendations.Clear();
        _arabicRecommendations.AddRange(
            arabicRecs.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));

        _englishRecommendations.Clear();
        _englishRecommendations.AddRange(
            englishRecs.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangeImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new DomainException("Tier image path is required.");
        }

        string trimmed = imagePath.Trim();
        if (trimmed.Length > MaximumImagePathLength)
        {
            throw new DomainException(
                $"Tier image path cannot exceed {MaximumImagePathLength} characters.");
        }

        ImagePath = trimmed;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public bool MatchesScore(int score) =>
        score >= MinScore && score <= MaxScore;

    private static void ValidateInputs(
        int screenOrder,
        string arabicTitle,
        string englishTitle,
        string arabicSubtitle,
        string englishSubtitle,
        string backgroundColor,
        string arabicButtonText,
        string englishButtonText,
        string imagePath,
        int minScore,
        int maxScore)
    {
        if (screenOrder < 1)
        {
            throw new DomainException("Screen order must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(arabicTitle))
        {
            throw new DomainException("Arabic title is required.");
        }

        if (string.IsNullOrWhiteSpace(englishTitle))
        {
            throw new DomainException("English title is required.");
        }

        if (string.IsNullOrWhiteSpace(arabicSubtitle))
        {
            throw new DomainException("Arabic subtitle is required.");
        }

        if (string.IsNullOrWhiteSpace(englishSubtitle))
        {
            throw new DomainException("English subtitle is required.");
        }

        if (string.IsNullOrWhiteSpace(backgroundColor))
        {
            throw new DomainException("Background color is required.");
        }

        if (string.IsNullOrWhiteSpace(arabicButtonText))
        {
            throw new DomainException("Arabic button text is required.");
        }

        if (string.IsNullOrWhiteSpace(englishButtonText))
        {
            throw new DomainException("English button text is required.");
        }

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new DomainException("Tier image path is required.");
        }

        if (arabicTitle.Trim().Length > MaximumTitleLength)
        {
            throw new DomainException($"Arabic title cannot exceed {MaximumTitleLength} characters.");
        }

        if (englishTitle.Trim().Length > MaximumTitleLength)
        {
            throw new DomainException($"English title cannot exceed {MaximumTitleLength} characters.");
        }

        if (arabicSubtitle.Trim().Length > MaximumSubtitleLength)
        {
            throw new DomainException($"Arabic subtitle cannot exceed {MaximumSubtitleLength} characters.");
        }

        if (englishSubtitle.Trim().Length > MaximumSubtitleLength)
        {
            throw new DomainException($"English subtitle cannot exceed {MaximumSubtitleLength} characters.");
        }

        if (backgroundColor.Trim().Length > MaximumColorLength)
        {
            throw new DomainException($"Background color cannot exceed {MaximumColorLength} characters.");
        }

        if (arabicButtonText.Trim().Length > MaximumButtonTextLength)
        {
            throw new DomainException($"Arabic button text cannot exceed {MaximumButtonTextLength} characters.");
        }

        if (englishButtonText.Trim().Length > MaximumButtonTextLength)
        {
            throw new DomainException($"English button text cannot exceed {MaximumButtonTextLength} characters.");
        }

        if (imagePath.Trim().Length > MaximumImagePathLength)
        {
            throw new DomainException($"Image path cannot exceed {MaximumImagePathLength} characters.");
        }

        if (minScore < 0)
        {
            throw new DomainException("Min score cannot be negative.");
        }

        if (maxScore < minScore)
        {
            throw new DomainException("Max score must be greater than or equal to min score.");
        }
    }
}