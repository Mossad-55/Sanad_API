using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.UnitTests.Families;

public sealed class AssessmentTierTests
{
    [Fact]
    public void Create_ShouldInstantiateTier_WhenDataIsValid()
    {
        var tier = AssessmentTier.Create(
            1,
            "رعاية مستقلة",
            "Independent Care",
            "المسن بحاجة إلى دعم طفيف",
            "The elderly needs slight assistance",
            "#4CAF50",
            "متابعة",
            "Continue",
            "illustrations/tier-green.svg",
            0,
            10,
            ["زيارات دورية"],
            ["Periodic visits"]);

        Assert.Equal("رعاية مستقلة", tier.ArabicTitle);
        Assert.Equal(0, tier.MinScore);
        Assert.Equal(10, tier.MaxScore);
        Assert.True(tier.MatchesScore(5));
        Assert.False(tier.MatchesScore(15));
    }

    [Fact]
    public void Create_ShouldThrow_WhenMaxScoreLessThanMinScore()
    {
        Assert.Throws<DomainException>(() => AssessmentTier.Create(
            1, "العنوان", "Title", "الوصف", "Desc", "#fff", "زر", "btn", "img.png",
            10, 5, ["rec"], ["rec"]));
    }
}