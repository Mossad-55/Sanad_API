using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class ProfessionalTitleTests
{
    [Fact]
    public void Create_ShouldCreateActiveProfessionalTitle()
    {
        ProfessionalTitle title =
            ProfessionalTitle.Create(
                "ممرض مسجل",
                "Registered Nurse");

        Assert.NotEqual(
            ProfessionalTitleId.Empty,
            title.Id);

        Assert.Equal(
            "ممرض مسجل",
            title.ArabicName);

        Assert.Equal(
            "Registered Nurse",
            title.EnglishName);

        Assert.True(title.IsActive);

        Assert.Equal(
            title.CreatedOnUtc,
            title.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldTrimNames()
    {
        ProfessionalTitle title =
            ProfessionalTitle.Create(
                "  ممرض مسجل  ",
                "  Registered Nurse  ");

        Assert.Equal(
            "ممرض مسجل",
            title.ArabicName);

        Assert.Equal(
            "Registered Nurse",
            title.EnglishName);
    }

    [Theory]
    [InlineData(null, "Registered Nurse")]
    [InlineData("", "Registered Nurse")]
    [InlineData("   ", "Registered Nurse")]
    [InlineData("ممرض مسجل", null)]
    [InlineData("ممرض مسجل", "")]
    [InlineData("ممرض مسجل", "   ")]
    public void Create_ShouldRejectMissingName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => ProfessionalTitle.Create(
                arabicName!,
                englishName!));
    }

    [Fact]
    public void Create_ShouldRejectNameThatIsTooLong()
    {
        string longName = new(
            'A',
            ProfessionalTitle.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => ProfessionalTitle.Create(
                "ممرض مسجل",
                longName));
    }

    [Fact]
    public void UpdateNames_ShouldUpdateNames()
    {
        ProfessionalTitle title =
            ProfessionalTitle.Create(
                "ممرض مسجل",
                "Registered Nurse");

        ProfessionalTitleId originalId =
            title.Id;

        title.UpdateNames(
            "ممرض أول",
            "Senior Nurse");

        Assert.Equal(originalId, title.Id);

        Assert.Equal(
            "ممرض أول",
            title.ArabicName);

        Assert.Equal(
            "Senior Nurse",
            title.EnglishName);

        Assert.True(title.IsActive);
    }

    [Fact]
    public void UpdateNames_ShouldBeAtomic()
    {
        ProfessionalTitle title =
            ProfessionalTitle.Create(
                "ممرض مسجل",
                "Registered Nurse");

        DateTime originalUpdatedOnUtc =
            title.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => title.UpdateNames(
                "ممرض أول",
                ""));

        Assert.Equal(
            "ممرض مسجل",
            title.ArabicName);

        Assert.Equal(
            "Registered Nurse",
            title.EnglishName);

        Assert.Equal(
            originalUpdatedOnUtc,
            title.UpdatedOnUtc);
    }

    [Fact]
    public void ActivateAndDeactivate_ShouldBeIdempotent()
    {
        ProfessionalTitle title =
            ProfessionalTitle.Create(
                "ممرض مسجل",
                "Registered Nurse");

        title.Deactivate();

        Assert.False(title.IsActive);

        DateTime deactivatedOnUtc =
            title.UpdatedOnUtc;

        title.Deactivate();

        Assert.Equal(
            deactivatedOnUtc,
            title.UpdatedOnUtc);

        title.Activate();

        Assert.True(title.IsActive);

        DateTime activatedOnUtc =
            title.UpdatedOnUtc;

        title.Activate();

        Assert.Equal(
            activatedOnUtc,
            title.UpdatedOnUtc);
    }
}