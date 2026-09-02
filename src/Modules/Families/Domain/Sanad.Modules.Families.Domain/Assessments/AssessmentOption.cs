using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Assessments;

public sealed class AssessmentOption : Entity<AssessmentOptionId>
{
    public const int MaximumTextLength = 300;
    public const int MaximumWeight = 100;

    private AssessmentOption()
    {
    }

    internal AssessmentOption(
        AssessmentOptionId id,
        AssessmentQuestionId questionId,
        int order,
        string arabicText,
        string englishText,
        int weight)
        : base(id)
    {
        QuestionId = questionId;
        Order = order;
        ArabicText = arabicText;
        EnglishText = englishText;
        Weight = weight;
    }

    public AssessmentQuestionId QuestionId { get; private set; }
    public int Order { get; private set; }
    public string ArabicText { get; private set; } = string.Empty;
    public string EnglishText { get; private set; } = string.Empty;
    public int Weight { get; private set; }

    internal static AssessmentOption Create(
        AssessmentQuestionId questionId,
        int order,
        string arabicText,
        string englishText,
        int weight)
    {
        if (questionId == AssessmentQuestionId.Empty)
        {
            throw new DomainException("Question ID is required for option.");
        }

        if (order < 1)
        {
            throw new DomainException("Option order must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(arabicText))
        {
            throw new DomainException("Arabic option text is required.");
        }

        if (string.IsNullOrWhiteSpace(englishText))
        {
            throw new DomainException("English option text is required.");
        }

        string trimmedAr = arabicText.Trim();
        if (trimmedAr.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"Arabic option text cannot exceed {MaximumTextLength} characters.");
        }

        string trimmedEn = englishText.Trim();
        if (trimmedEn.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"English option text cannot exceed {MaximumTextLength} characters.");
        }

        if (weight < 0 || weight > MaximumWeight)
        {
            throw new DomainException(
                $"Option weight must be between 0 and {MaximumWeight}.");
        }

        return new AssessmentOption(
            AssessmentOptionId.New(),
            questionId,
            order,
            trimmedAr,
            trimmedEn,
            weight);
    }

    internal void Update(
        int order,
        string arabicText,
        string englishText,
        int weight)
    {
        if (order < 1)
        {
            throw new DomainException("Option order must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(arabicText))
        {
            throw new DomainException("Arabic option text is required.");
        }

        if (string.IsNullOrWhiteSpace(englishText))
        {
            throw new DomainException("English option text is required.");
        }

        string trimmedAr = arabicText.Trim();
        if (trimmedAr.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"Arabic option text cannot exceed {MaximumTextLength} characters.");
        }

        string trimmedEn = englishText.Trim();
        if (trimmedEn.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"English option text cannot exceed {MaximumTextLength} characters.");
        }

        if (weight < 0 || weight > MaximumWeight)
        {
            throw new DomainException(
                $"Option weight must be between 0 and {MaximumWeight}.");
        }

        Order = order;
        ArabicText = trimmedAr;
        EnglishText = trimmedEn;
        Weight = weight;
    }
}