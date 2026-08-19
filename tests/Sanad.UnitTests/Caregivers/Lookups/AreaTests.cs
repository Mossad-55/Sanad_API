using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class AreaTests
{
    [Fact]
    public void Create_ShouldCreateActiveArea()
    {
        CityId cityId = CityId.New();

        Area area = Area.Create(
            cityId,
            "شبرا",
            "Shubra");

        Assert.NotEqual(
            AreaId.Empty,
            area.Id);

        Assert.Equal(
            cityId,
            area.CityId);

        Assert.Equal(
            "شبرا",
            area.ArabicName);

        Assert.Equal(
            "Shubra",
            area.EnglishName);

        Assert.True(area.IsActive);

        Assert.Equal(
            area.CreatedOnUtc,
            area.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldRejectEmptyCityId()
    {
        Assert.Throws<DomainException>(
            () => Area.Create(
                CityId.Empty,
                "شبرا",
                "Shubra"));
    }

    [Fact]
    public void Create_ShouldTrimAreaNames()
    {
        Area area = Area.Create(
            CityId.New(),
            "  شبرا  ",
            "  Shubra  ");

        Assert.Equal(
            "شبرا",
            area.ArabicName);

        Assert.Equal(
            "Shubra",
            area.EnglishName);
    }

    [Theory]
    [InlineData(null, "Shubra")]
    [InlineData("", "Shubra")]
    [InlineData("   ", "Shubra")]
    [InlineData("شبرا", null)]
    [InlineData("شبرا", "")]
    [InlineData("شبرا", "   ")]
    public void Create_ShouldRejectMissingAreaName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => Area.Create(
                CityId.New(),
                arabicName!,
                englishName!));
    }

    [Fact]
    public void Create_ShouldRejectArabicNameThatIsTooLong()
    {
        string longArabicName = new(
            'أ',
            Area.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Area.Create(
                CityId.New(),
                longArabicName,
                "Shubra"));
    }

    [Fact]
    public void Create_ShouldRejectEnglishNameThatIsTooLong()
    {
        string longEnglishName = new(
            'A',
            Area.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Area.Create(
                CityId.New(),
                "شبرا",
                longEnglishName));
    }

    [Fact]
    public void UpdateNames_ShouldUpdateAndTrimNames()
    {
        CityId cityId = CityId.New();

        Area area = Area.Create(
            cityId,
            "شبرا",
            "Shubra");

        AreaId originalId = area.Id;

        area.UpdateNames(
            "  منطقة شبرا  ",
            "  Shubra Area  ");

        Assert.Equal(
            originalId,
            area.Id);

        Assert.Equal(
            cityId,
            area.CityId);

        Assert.Equal(
            "منطقة شبرا",
            area.ArabicName);

        Assert.Equal(
            "Shubra Area",
            area.EnglishName);

        Assert.True(area.IsActive);

        Assert.True(
            area.UpdatedOnUtc >=
            area.CreatedOnUtc);
    }

    [Fact]
    public void UpdateNames_ShouldNotPartiallyUpdate_WhenEnglishNameIsInvalid()
    {
        Area area = Area.Create(
            CityId.New(),
            "شبرا",
            "Shubra");

        DateTime originalUpdatedOnUtc =
            area.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => area.UpdateNames(
                "منطقة شبرا",
                ""));

        Assert.Equal(
            "شبرا",
            area.ArabicName);

        Assert.Equal(
            "Shubra",
            area.EnglishName);

        Assert.Equal(
            originalUpdatedOnUtc,
            area.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldMakeAreaInactive()
    {
        Area area = Area.Create(
            CityId.New(),
            "شبرا",
            "Shubra");

        area.Deactivate();

        Assert.False(area.IsActive);

        Assert.True(
            area.UpdatedOnUtc >=
            area.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldMakeAreaActive()
    {
        Area area = Area.Create(
            CityId.New(),
            "شبرا",
            "Shubra");

        area.Deactivate();
        area.Activate();

        Assert.True(area.IsActive);

        Assert.True(
            area.UpdatedOnUtc >=
            area.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldDoNothing_WhenAlreadyActive()
    {
        Area area = Area.Create(
            CityId.New(),
            "شبرا",
            "Shubra");

        DateTime updatedOnUtc =
            area.UpdatedOnUtc;

        area.Activate();

        Assert.True(area.IsActive);

        Assert.Equal(
            updatedOnUtc,
            area.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldDoNothing_WhenAlreadyInactive()
    {
        Area area = Area.Create(
            CityId.New(),
            "شبرا",
            "Shubra");

        area.Deactivate();

        DateTime updatedOnUtc =
            area.UpdatedOnUtc;

        area.Deactivate();

        Assert.False(area.IsActive);

        Assert.Equal(
            updatedOnUtc,
            area.UpdatedOnUtc);
    }
}