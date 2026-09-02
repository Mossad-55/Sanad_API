using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Assessments;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class FamilyAssessmentTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task GetQuestions_ShouldNotIncludeOptionWeights_ForFamilyApp()
    {
        using var dbContext = CreateDbContext();
        var handler = new GetFamilyAssessmentQuestionsQueryHandler(dbContext);

        var question = AssessmentQuestion.Create(1, "سؤال", "Question", isRequired: true);
        question.SetOptions([(1, "خيار 1", "Opt 1", 5), (2, "خيار 2", "Opt 2", 10)]);

        dbContext.AssessmentQuestions.Add(question);
        await dbContext.SaveChangesAsync();

        var result = await handler.Handle(new GetFamilyAssessmentQuestionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(2, result.Value[0].Options.Count);
    }

    [Fact]
    public async Task SubmitAssessment_ShouldCalculateScoreAndMatchTier_WhenValid()
    {
        using var dbContext = CreateDbContext();
        var userId = UserId.New();
        var family = Family.Create(userId, "عائلة السند");
        dbContext.Families.Add(family);

        var question = AssessmentQuestion.Create(1, "الحركة", "Mobility", isRequired: true);
        question.SetOptions([(1, "مستقل", "Independent", 0), (2, "يحتاج مساعدة", "Needs Help", 5)]);
        dbContext.AssessmentQuestions.Add(question);

        var tier = AssessmentTier.Create(
            1, "رعاية متوسطة", "Moderate", "وصف", "Sub", "#ff0", "متابعة", "Next", "tier.png",
            4, 8, ["متابعة"], ["Follow-up"]);
        dbContext.AssessmentTiers.Add(tier);

        await dbContext.SaveChangesAsync();

        var selectedOption = question.Options.First(o => o.Weight == 5);

        var handler = new SubmitAssessmentCommandHandler(dbContext);
        var command = new SubmitAssessmentCommand(
            userId,
            null,
            [new AssessmentAnswerInput(question.Id, selectedOption.Id)],
            DateTime.UtcNow);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.TotalScore);
        Assert.Equal("رعاية متوسطة", result.Value.Tier.ArabicTitle);
    }

    [Fact]
    public async Task SubmitAssessment_ShouldFail_WhenRequiredQuestionNotAnswered()
    {
        using var dbContext = CreateDbContext();
        var userId = UserId.New();
        var family = Family.Create(userId, "عائلة السند");
        dbContext.Families.Add(family);

        var q1 = AssessmentQuestion.Create(1, "سؤال إجباري", "Required", isRequired: true);
        q1.SetOptions([(1, "أ", "A", 1), (2, "ب", "B", 2)]);
        var q2 = AssessmentQuestion.Create(2, "سؤال اختياري", "Optional", isRequired: false);
        q2.SetOptions([(1, "أ", "A", 1), (2, "ب", "B", 2)]);

        dbContext.AssessmentQuestions.AddRange(q1, q2);
        dbContext.AssessmentTiers.Add(AssessmentTier.Create(1, "T", "T", "D", "D", "#fff", "B", "B", "i.png", 0, 10, ["r"], ["r"]));
        await dbContext.SaveChangesAsync();

        var handler = new SubmitAssessmentCommandHandler(dbContext);
        // Only answered q2, missing required q1
        var command = new SubmitAssessmentCommand(
            userId,
            null,
            [new AssessmentAnswerInput(q2.Id, q2.Options.First().Id)],
            DateTime.UtcNow);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Assessment.InvalidSubmission", result.Error.Code);
    }
}