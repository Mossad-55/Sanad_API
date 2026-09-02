using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Assessments;
using Sanad.Modules.Families.Domain.Assessments;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

public sealed class AdminAssessmentTiersTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task CreateTier_ShouldPersistTier_WhenValid()
    {
        using var dbContext = CreateDbContext();
        var handler = new CreateAssessmentTierCommandHandler(dbContext);

        var command = new CreateAssessmentTierCommand(
            1,
            "رعاية مستقلة",
            "Independent Care",
            "وصف عربي",
            "English subtitle",
            "#4CAF50",
            "متابعة",
            "Continue",
            "assessment-tiers/tier1.svg",
            0,
            10,
            ["توصية 1"],
            ["Rec 1"],
            true);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("رعاية مستقلة", result.Value.ArabicTitle);
        Assert.Equal(0, result.Value.MinScore);
        Assert.Equal(10, result.Value.MaxScore);
    }

    [Fact]
    public async Task UpdateTier_ShouldReturnNotFound_WhenTierDoesNotExist()
    {
        using var dbContext = CreateDbContext();
        var handler = new UpdateAssessmentTierCommandHandler(dbContext);

        var command = new UpdateAssessmentTierCommand(
            AssessmentTierId.New(),
            1,
            "عنوان",
            "Title",
            "وصف",
            "Sub",
            "#fff",
            "زر",
            "Btn",
            null,
            0,
            10,
            ["توصية"],
            ["Rec"]);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Assessment.TierNotFound", result.Error.Code);
    }
}