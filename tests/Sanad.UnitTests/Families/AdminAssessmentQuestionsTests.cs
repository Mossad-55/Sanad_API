using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Assessments;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class AdminAssessmentQuestionsTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task CreateQuestion_ShouldPersistQuestionWithOptions_WhenValid()
    {
        using var dbContext = CreateDbContext();
        var handler = new CreateAssessmentQuestionCommandHandler(dbContext);

        var options = new List<AdminOptionInput>
        {
            new(1, "خيار 1", "Option 1", 0),
            new(2, "خيار 2", "Option 2", 3)
        };

        var command = new CreateAssessmentQuestionCommand(
            1,
            "سؤال تجريبي",
            "Test Question",
            true,
            true,
            options);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("سؤال تجريبي", result.Value.ArabicText);
        Assert.Equal(2, result.Value.Options.Count);
    }

    [Fact]
    public async Task UpdateQuestion_ShouldReturnNotFound_WhenQuestionDoesNotExist()
    {
        using var dbContext = CreateDbContext();
        var handler = new UpdateAssessmentQuestionCommandHandler(dbContext);

        var command = new UpdateAssessmentQuestionCommand(
            AssessmentQuestionId.New(),
            1,
            "سؤال",
            "Question",
            true,
            [new(1, "أ", "A", 0), new(2, "ب", "B", 1)]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Assessment.QuestionNotFound", result.Error.Code);
    }
}