using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.UnitTests.Families;

public sealed class AssessmentQuestionTests
{
    [Fact]
    public void Create_ShouldInstantiateQuestion_WhenDataIsValid()
    {
        var question = AssessmentQuestion.Create(
            1,
            "هل يستطيع المسن الحركة بمفرده؟",
            "Can the elderly move independently?",
            isRequired: true);

        Assert.Equal(1, question.Order);
        Assert.Equal("هل يستطيع المسن الحركة بمفرده؟", question.ArabicText);
        Assert.Equal("Can the elderly move independently?", question.EnglishText);
        Assert.True(question.IsRequired);
        Assert.True(question.IsActive);
        Assert.Empty(question.Options);
    }

    [Fact]
    public void SetOptions_ShouldSetChildOptions_WhenValid()
    {
        var question = AssessmentQuestion.Create(
            1,
            "سؤال",
            "Question",
            isRequired: true);

        var options = new List<(int order, string ar, string en, int weight)>
        {
            (1, "دائماً", "Always", 0),
            (2, "أحياناً", "Sometimes", 2),
            (3, "أبداً", "Never", 5)
        };

        question.SetOptions(options);

        Assert.Equal(3, question.Options.Count);
        Assert.Contains(question.Options, o => o.ArabicText == "دائماً" && o.Weight == 0);
    }

    [Fact]
    public void SetOptions_ShouldThrow_WhenFewerThanTwoOptions()
    {
        var question = AssessmentQuestion.Create(1, "سؤال", "Question", true);

        var options = new List<(int order, string ar, string en, int weight)>
        {
            (1, "خيار وحيد", "Single Option", 0)
        };

        Assert.Throws<DomainException>(() => question.SetOptions(options));
    }
}