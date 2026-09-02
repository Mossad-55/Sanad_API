using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Assessments;

public sealed class CareAssessment : AggregateRoot<CareAssessmentId>
{
    private readonly List<CareAssessmentAnswer> _answers = [];

    private CareAssessment()
    {
    }

    private CareAssessment(
        CareAssessmentId id,
        FamilyId familyId,
        ElderlyId? elderlyId,
        AssessmentTierId tierId,
        int totalScore,
        DateTime completedOnUtc)
        : base(id)
    {
        FamilyId = familyId;
        ElderlyId = elderlyId;
        AssessmentTierId = tierId;
        TotalScore = totalScore;
        CompletedOnUtc = completedOnUtc;
    }

    public FamilyId FamilyId { get; private set; }
    public ElderlyId? ElderlyId { get; private set; }
    public AssessmentTierId AssessmentTierId { get; private set; }
    public int TotalScore { get; private set; }
    public DateTime CompletedOnUtc { get; private set; }

    public IReadOnlyCollection<CareAssessmentAnswer> Answers =>
        _answers.AsReadOnly();

    public static CareAssessment Create(
        FamilyId familyId,
        ElderlyId? elderlyId,
        AssessmentTierId tierId,
        int totalScore,
        IEnumerable<(AssessmentQuestionId questionId, AssessmentOptionId optionId, int scoreSnapshot)> answers,
        DateTime completedOnUtc)
    {
        if (familyId.Value == Guid.Empty)
        {
            throw new DomainException("Family ID is required for assessment.");
        }

        if (tierId.Value == Guid.Empty)
        {
            throw new DomainException("Assessment tier ID is required.");
        }

        if (totalScore < 0)
        {
            throw new DomainException("Total score cannot be negative.");
        }

        var answerList = answers.ToList();
        if (answerList.Count == 0)
        {
            throw new DomainException("Assessment must contain at least one answered question.");
        }

        var assessment = new CareAssessment(
            CareAssessmentId.New(),
            familyId,
            elderlyId,
            tierId,
            totalScore,
            completedOnUtc);

        foreach (var (qId, oId, score) in answerList)
        {
            assessment._answers.Add(CareAssessmentAnswer.Create(
                assessment.Id,
                qId,
                oId,
                score));
        }

        return assessment;
    }

    public void LinkToElderly(ElderlyId elderlyId)
    {
        if (elderlyId.Value == Guid.Empty)
        {
            throw new DomainException("Elderly ID cannot be empty.");
        }

        ElderlyId = elderlyId;
    }
}