using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Assessments;

public sealed class AssessmentQuestion : AggregateRoot<AssessmentQuestionId>
{
    public const int MaximumTextLength = 500;
    public const int MinimumOptionsCount = 2;
    public const int MaximumOptionsCount = 10;

    private readonly List<AssessmentOption> _options = [];

    private AssessmentQuestion()
    {
    }

    private AssessmentQuestion(
        AssessmentQuestionId id,
        int order,
        string arabicText,
        string englishText,
        bool isRequired,
        bool isActive)
        : base(id)
    {
        Order = order;
        ArabicText = arabicText;
        EnglishText = englishText;
        IsRequired = isRequired;
        IsActive = isActive;
        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public int Order { get; private set; }
    public string ArabicText { get; private set; } = string.Empty;
    public string EnglishText { get; private set; } = string.Empty;
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<AssessmentOption> Options =>
        _options.AsReadOnly();

    public static AssessmentQuestion Create(
        int order,
        string arabicText,
        string englishText,
        bool isRequired,
        bool isActive = true)
    {
        if (order < 1)
        {
            throw new DomainException("Question order must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(arabicText))
        {
            throw new DomainException("Arabic question text is required.");
        }

        if (string.IsNullOrWhiteSpace(englishText))
        {
            throw new DomainException("English question text is required.");
        }

        string trimmedAr = arabicText.Trim();
        if (trimmedAr.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"Arabic question text cannot exceed {MaximumTextLength} characters.");
        }

        string trimmedEn = englishText.Trim();
        if (trimmedEn.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"English question text cannot exceed {MaximumTextLength} characters.");
        }

        return new AssessmentQuestion(
            AssessmentQuestionId.New(),
            order,
            trimmedAr,
            trimmedEn,
            isRequired,
            isActive);
    }

    public void UpdateDetails(
        int order,
        string arabicText,
        string englishText,
        bool isRequired)
    {
        if (order < 1)
        {
            throw new DomainException("Question order must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(arabicText))
        {
            throw new DomainException("Arabic question text is required.");
        }

        if (string.IsNullOrWhiteSpace(englishText))
        {
            throw new DomainException("English question text is required.");
        }

        string trimmedAr = arabicText.Trim();
        if (trimmedAr.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"Arabic question text cannot exceed {MaximumTextLength} characters.");
        }

        string trimmedEn = englishText.Trim();
        if (trimmedEn.Length > MaximumTextLength)
        {
            throw new DomainException(
                $"English question text cannot exceed {MaximumTextLength} characters.");
        }

        Order = order;
        ArabicText = trimmedAr;
        EnglishText = trimmedEn;
        IsRequired = isRequired;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SetOptions(
        IEnumerable<(int order, string arabicText, string englishText, int weight)> optionInputs)
    {
        ArgumentNullException.ThrowIfNull(optionInputs);

        var list = optionInputs.ToList();

        if (list.Count < MinimumOptionsCount)
        {
            throw new DomainException(
                $"A question must have at least {MinimumOptionsCount} options.");
        }

        if (list.Count > MaximumOptionsCount)
        {
            throw new DomainException(
                $"A question cannot have more than {MaximumOptionsCount} options.");
        }

        _options.Clear();

        foreach (var (order, ar, en, weight) in list)
        {
            _options.Add(AssessmentOption.Create(
                Id,
                order,
                ar,
                en,
                weight));
        }

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
}