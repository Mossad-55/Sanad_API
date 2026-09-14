using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Domain.Help;

/// <summary>
/// One bilingual FAQ entry for exactly one app audience. FAQs are not
/// versioned; activation state controls app visibility.
/// </summary>
public sealed class HelpFaq : AggregateRoot<HelpFaqId>
{
    public const int MaximumQuestionLength = 300;
    public const int MaximumAnswerLength = 3000;

    private HelpFaq()
    {
    }

    private HelpFaq(
        HelpFaqId id,
        LegalAudience audience,
        string arabicQuestion,
        string englishQuestion,
        string arabicAnswer,
        string englishAnswer,
        int displayOrder,
        bool isActive,
        DateTime createdOnUtc)
        : base(id)
    {
        Audience = audience;
        ArabicQuestion = arabicQuestion;
        EnglishQuestion = englishQuestion;
        ArabicAnswer = arabicAnswer;
        EnglishAnswer = englishAnswer;
        DisplayOrder = displayOrder;
        IsActive = isActive;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public LegalAudience Audience { get; private set; }
    public string ArabicQuestion { get; private set; } = string.Empty;
    public string EnglishQuestion { get; private set; } = string.Empty;
    public string ArabicAnswer { get; private set; } = string.Empty;
    public string EnglishAnswer { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static HelpFaq Create(
        LegalAudience audience,
        string arabicQuestion,
        string englishQuestion,
        string arabicAnswer,
        string englishAnswer,
        int displayOrder,
        bool isActive)
    {
        if (!audience.IsDefined())
        {
            throw new DomainException(
                "Audience is not a supported app audience.");
        }

        EnsureDisplayOrderIsValid(displayOrder);

        return new HelpFaq(
            HelpFaqId.New(),
            audience,
            NormalizeRequiredText(
                arabicQuestion,
                "Arabic question",
                MaximumQuestionLength),
            NormalizeRequiredText(
                englishQuestion,
                "English question",
                MaximumQuestionLength),
            NormalizeRequiredText(
                arabicAnswer,
                "Arabic answer",
                MaximumAnswerLength),
            NormalizeRequiredText(
                englishAnswer,
                "English answer",
                MaximumAnswerLength),
            displayOrder,
            isActive,
            DateTime.UtcNow);
    }

    public void UpdateContent(
        LegalAudience audience,
        string arabicQuestion,
        string englishQuestion,
        string arabicAnswer,
        string englishAnswer,
        int displayOrder,
        bool isActive)
    {
        if (!audience.IsDefined())
        {
            throw new DomainException(
                "Audience is not a supported app audience.");
        }

        EnsureDisplayOrderIsValid(displayOrder);

        Audience = audience;
        ArabicQuestion = NormalizeRequiredText(
            arabicQuestion,
            "Arabic question",
            MaximumQuestionLength);
        EnglishQuestion = NormalizeRequiredText(
            englishQuestion,
            "English question",
            MaximumQuestionLength);
        ArabicAnswer = NormalizeRequiredText(
            arabicAnswer,
            "Arabic answer",
            MaximumAnswerLength);
        EnglishAnswer = NormalizeRequiredText(
            englishAnswer,
            "English answer",
            MaximumAnswerLength);
        DisplayOrder = displayOrder;
        IsActive = isActive;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Idempotent activation.
    /// </summary>
    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Idempotent deactivation.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static void EnsureDisplayOrderIsValid(int displayOrder)
    {
        if (displayOrder < 0)
        {
            throw new DomainException(
                "Display order cannot be negative.");
        }
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
}
