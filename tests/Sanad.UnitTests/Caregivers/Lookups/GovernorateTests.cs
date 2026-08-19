using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class GovernorateTests
{
    [Fact]
    public void Create_ShouldCreateActiveGovernorate()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        Assert.NotEqual(
            GovernorateId.Empty,
            governorate.Id);

        Assert.Equal(
            "البحيرة",
            governorate.ArabicName);

        Assert.Equal(
            "Beheira",
            governorate.EnglishName);

        Assert.True(governorate.IsActive);

        Assert.Equal(
            governorate.CreatedOnUtc,
            governorate.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldTrimGovernorateNames()
    {
        Governorate governorate =
            Governorate.Create(
                "  البحيرة  ",
                "  Beheira  ");

        Assert.Equal(
            "البحيرة",
            governorate.ArabicName);

        Assert.Equal(
            "Beheira",
            governorate.EnglishName);
    }

    [Theory]
    [InlineData(null, "Beheira")]
    [InlineData("", "Beheira")]
    [InlineData("   ", "Beheira")]
    [InlineData("البحيرة", null)]
    [InlineData("البحيرة", "")]
    [InlineData("البحيرة", "   ")]
    public void Create_ShouldRejectMissingGovernorateName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => Governorate.Create(
                arabicName!,
                englishName!));
    }

    [Fact]
    public void Create_ShouldRejectArabicNameThatIsTooLong()
    {
        string longArabicName = new(
            'أ',
            Governorate.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Governorate.Create(
                longArabicName,
                "Beheira"));
    }

    [Fact]
    public void Create_ShouldRejectEnglishNameThatIsTooLong()
    {
        string longEnglishName = new(
            'A',
            Governorate.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Governorate.Create(
                "البحيرة",
                longEnglishName));
    }

    [Fact]
    public void UpdateNames_ShouldUpdateAndTrimNames()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        GovernorateId originalId =
            governorate.Id;

        governorate.UpdateNames(
            "  محافظة البحيرة  ",
            "  Beheira Governorate  ");

        Assert.Equal(
            originalId,
            governorate.Id);

        Assert.Equal(
            "محافظة البحيرة",
            governorate.ArabicName);

        Assert.Equal(
            "Beheira Governorate",
            governorate.EnglishName);

        Assert.True(governorate.IsActive);

        Assert.True(
            governorate.UpdatedOnUtc >=
            governorate.CreatedOnUtc);
    }

    [Fact]
    public void UpdateNames_ShouldNotPartiallyUpdate_WhenEnglishNameIsInvalid()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        DateTime originalUpdatedOnUtc =
            governorate.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => governorate.UpdateNames(
                "محافظة البحيرة",
                ""));

        Assert.Equal(
            "البحيرة",
            governorate.ArabicName);

        Assert.Equal(
            "Beheira",
            governorate.EnglishName);

        Assert.Equal(
            originalUpdatedOnUtc,
            governorate.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldMakeGovernorateInactive()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        governorate.Deactivate();

        Assert.False(governorate.IsActive);

        Assert.True(
            governorate.UpdatedOnUtc >=
            governorate.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldMakeGovernorateActive()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        governorate.Deactivate();
        governorate.Activate();

        Assert.True(governorate.IsActive);

        Assert.True(
            governorate.UpdatedOnUtc >=
            governorate.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldDoNothing_WhenAlreadyActive()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        DateTime updatedOnUtc =
            governorate.UpdatedOnUtc;

        governorate.Activate();

        Assert.True(governorate.IsActive);

        Assert.Equal(
            updatedOnUtc,
            governorate.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldDoNothing_WhenAlreadyInactive()
    {
        Governorate governorate =
            Governorate.Create(
                "البحيرة",
                "Beheira");

        governorate.Deactivate();

        DateTime updatedOnUtc =
            governorate.UpdatedOnUtc;

        governorate.Deactivate();

        Assert.False(governorate.IsActive);

        Assert.Equal(
            updatedOnUtc,
            governorate.UpdatedOnUtc);
    }
}