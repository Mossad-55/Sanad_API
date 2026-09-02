using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Assessments;

public sealed class CareAssessmentAnswer : Entity<Guid>
{
    private CareAssessmentAnswer()
    {
    }

    internal CareAssessmentAnswer(
        Guid id,
        CareAssessmentId assessmentId,
        AssessmentQuestionId questionId,
        AssessmentOptionId selectedOptionId,
        int scoreSnapshot)
        : base(id)
    {
        AssessmentId = assessmentId;
        QuestionId = questionId;
        SelectedOptionId = selectedOptionId;
        ScoreSnapshot = scoreSnapshot;
    }

    public CareAssessmentId AssessmentId { get; private set; }
    public AssessmentQuestionId QuestionId { get; private set; }
    public AssessmentOptionId SelectedOptionId { get; private set; }
    public int ScoreSnapshot { get; private set; }

    internal static CareAssessmentAnswer Create(
        CareAssessmentId assessmentId,
        AssessmentQuestionId questionId,
        AssessmentOptionId selectedOptionId,
        int scoreSnapshot)
    {
        if (questionId == AssessmentQuestionId.Empty)
        {
            throw new DomainException("Question ID is required for answer.");
        }

        if (selectedOptionId == AssessmentOptionId.Empty)
        {
            throw new DomainException("Selected option ID is required for answer.");
        }

        if (scoreSnapshot < 0)
        {
            throw new DomainException("Score snapshot cannot be negative.");
        }

        return new CareAssessmentAnswer(
            Guid.CreateVersion7(),
            assessmentId,
            questionId,
            selectedOptionId,
            scoreSnapshot);
    }
}