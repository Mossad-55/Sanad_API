using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.UnitTests.Families;

public sealed class CareAssessmentTests
{
    [Fact]
    public void Create_ShouldInstantiateAssessmentWithAnswers_WhenValid()
    {
        var familyId = FamilyId.New();
        var tierId = AssessmentTierId.New();
        var qId = AssessmentQuestionId.New();
        var oId = AssessmentOptionId.New();

        var answers = new List<(AssessmentQuestionId, AssessmentOptionId, int)>
        {
            (qId, oId, 3)
        };

        var assessment = CareAssessment.Create(
            familyId,
            null,
            tierId,
            3,
            answers,
            DateTime.UtcNow);

        Assert.Equal(familyId, assessment.FamilyId);
        Assert.Null(assessment.ElderlyId);
        Assert.Equal(3, assessment.TotalScore);
        Assert.Single(assessment.Answers);
    }

    [Fact]
    public void LinkToElderly_ShouldSetElderlyId_WhenProvided()
    {
        var assessment = CareAssessment.Create(
            FamilyId.New(),
            null,
            AssessmentTierId.New(),
            0,
            [(AssessmentQuestionId.New(), AssessmentOptionId.New(), 0)],
            DateTime.UtcNow);

        var elderlyId = ElderlyId.New();
        assessment.LinkToElderly(elderlyId);

        Assert.Equal(elderlyId, assessment.ElderlyId);
    }

    [Fact]
    public void Create_ShouldThrow_WhenEmptyAnswers()
    {
        Assert.Throws<DomainException>(() => CareAssessment.Create(
            FamilyId.New(),
            null,
            AssessmentTierId.New(),
            0,
            [],
            DateTime.UtcNow));
    }
}