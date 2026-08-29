using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class AcademicDegreeTests
{
    [Fact]
    public void Create_ShouldCreateActiveAcademicDegree()
    {
        AcademicDegree degree =
            AcademicDegree.Create(
                "بكالوريوس تمريض",
                "Bachelor of Nursing",
                true);

        Assert.NotEqual(
            AcademicDegreeId.Empty,
            degree.Id);

        Assert.Equal(
            "بكالوريوس تمريض",
            degree.ArabicName);

        Assert.Equal(
            "Bachelor of Nursing",
            degree.EnglishName);

        Assert.True(degree.IsActive);

        Assert.Equal(
            degree.CreatedOnUtc,
            degree.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldTrimNames()
    {
        AcademicDegree degree =
            AcademicDegree.Create(
                "  بكالوريوس تمريض  ",
                "  Bachelor of Nursing  ",
                true);

        Assert.Equal(
            "بكالوريوس تمريض",
            degree.ArabicName);

        Assert.Equal(
            "Bachelor of Nursing",
            degree.EnglishName);
    }

    [Theory]
    [InlineData(null, "Bachelor of Nursing")]
    [InlineData("", "Bachelor of Nursing")]
    [InlineData("   ", "Bachelor of Nursing")]
    [InlineData("بكالوريوس تمريض", null)]
    [InlineData("بكالوريوس تمريض", "")]
    [InlineData("بكالوريوس تمريض", "   ")]
    public void Create_ShouldRejectMissingName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => AcademicDegree.Create(
                arabicName!,
                englishName!,
                true));
    }

    [Fact]
    public void Create_ShouldRejectNameThatIsTooLong()
    {
        string longName = new(
            'A',
            AcademicDegree.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => AcademicDegree.Create(
                "بكالوريوس تمريض",
                longName,
                true));
    }

    [Fact]
    public void UpdateNames_ShouldUpdateNames()
    {
        AcademicDegree degree =
            AcademicDegree.Create(
                "بكالوريوس تمريض",
                "Bachelor of Nursing",
                true);

        AcademicDegreeId originalId =
            degree.Id;

        degree.UpdateNames(
            "ماجستير تمريض",
            "Master of Nursing");

        Assert.Equal(originalId, degree.Id);

        Assert.Equal(
            "ماجستير تمريض",
            degree.ArabicName);

        Assert.Equal(
            "Master of Nursing",
            degree.EnglishName);

        Assert.True(degree.IsActive);
    }

    [Fact]
    public void UpdateNames_ShouldBeAtomic()
    {
        AcademicDegree degree =
            AcademicDegree.Create(
                "بكالوريوس تمريض",
                "Bachelor of Nursing",
                true);

        DateTime originalUpdatedOnUtc =
            degree.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => degree.UpdateNames(
                "ماجستير تمريض",
                ""));

        Assert.Equal(
            "بكالوريوس تمريض",
            degree.ArabicName);

        Assert.Equal(
            "Bachelor of Nursing",
            degree.EnglishName);

        Assert.Equal(
            originalUpdatedOnUtc,
            degree.UpdatedOnUtc);
    }

    [Fact]
    public void ActivateAndDeactivate_ShouldBeIdempotent()
    {
        AcademicDegree degree =
            AcademicDegree.Create(
                "بكالوريوس تمريض",
                "Bachelor of Nursing",
                false);

        degree.Deactivate();

        Assert.False(degree.IsActive);

        DateTime deactivatedOnUtc =
            degree.UpdatedOnUtc;

        degree.Deactivate();

        Assert.Equal(
            deactivatedOnUtc,
            degree.UpdatedOnUtc);

        degree.Activate();

        Assert.True(degree.IsActive);

        DateTime activatedOnUtc =
            degree.UpdatedOnUtc;

        degree.Activate();

        Assert.Equal(
            activatedOnUtc,
            degree.UpdatedOnUtc);
    }
}