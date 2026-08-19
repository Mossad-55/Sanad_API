using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class CityTests
{
    [Fact]
    public void Create_ShouldCreateActiveCity()
    {
        GovernorateId governorateId =
            GovernorateId.New();

        City city = City.Create(
            governorateId,
            "دمنهور",
            "Damanhur");

        Assert.NotEqual(
            CityId.Empty,
            city.Id);

        Assert.Equal(
            governorateId,
            city.GovernorateId);

        Assert.Equal(
            "دمنهور",
            city.ArabicName);

        Assert.Equal(
            "Damanhur",
            city.EnglishName);

        Assert.True(city.IsActive);

        Assert.Equal(
            city.CreatedOnUtc,
            city.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldRejectEmptyGovernorateId()
    {
        Assert.Throws<DomainException>(
            () => City.Create(
                GovernorateId.Empty,
                "دمنهور",
                "Damanhur"));
    }

    [Fact]
    public void Create_ShouldTrimCityNames()
    {
        City city = City.Create(
            GovernorateId.New(),
            "  دمنهور  ",
            "  Damanhur  ");

        Assert.Equal(
            "دمنهور",
            city.ArabicName);

        Assert.Equal(
            "Damanhur",
            city.EnglishName);
    }

    [Theory]
    [InlineData(null, "Damanhur")]
    [InlineData("", "Damanhur")]
    [InlineData("   ", "Damanhur")]
    [InlineData("دمنهور", null)]
    [InlineData("دمنهور", "")]
    [InlineData("دمنهور", "   ")]
    public void Create_ShouldRejectMissingCityName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => City.Create(
                GovernorateId.New(),
                arabicName!,
                englishName!));
    }

    [Fact]
    public void Create_ShouldRejectArabicNameThatIsTooLong()
    {
        string longArabicName = new(
            'أ',
            City.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => City.Create(
                GovernorateId.New(),
                longArabicName,
                "Damanhur"));
    }

    [Fact]
    public void Create_ShouldRejectEnglishNameThatIsTooLong()
    {
        string longEnglishName = new(
            'A',
            City.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => City.Create(
                GovernorateId.New(),
                "دمنهور",
                longEnglishName));
    }

    [Fact]
    public void UpdateNames_ShouldUpdateAndTrimNames()
    {
        GovernorateId governorateId =
            GovernorateId.New();

        City city = City.Create(
            governorateId,
            "دمنهور",
            "Damanhur");

        CityId originalId = city.Id;

        city.UpdateNames(
            "  مدينة دمنهور  ",
            "  Damanhur City  ");

        Assert.Equal(originalId, city.Id);

        Assert.Equal(
            governorateId,
            city.GovernorateId);

        Assert.Equal(
            "مدينة دمنهور",
            city.ArabicName);

        Assert.Equal(
            "Damanhur City",
            city.EnglishName);

        Assert.True(city.IsActive);

        Assert.True(
            city.UpdatedOnUtc >=
            city.CreatedOnUtc);
    }

    [Fact]
    public void UpdateNames_ShouldNotPartiallyUpdate_WhenEnglishNameIsInvalid()
    {
        City city = City.Create(
            GovernorateId.New(),
            "دمنهور",
            "Damanhur");

        DateTime originalUpdatedOnUtc =
            city.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => city.UpdateNames(
                "مدينة دمنهور",
                ""));

        Assert.Equal(
            "دمنهور",
            city.ArabicName);

        Assert.Equal(
            "Damanhur",
            city.EnglishName);

        Assert.Equal(
            originalUpdatedOnUtc,
            city.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldMakeCityInactive()
    {
        City city = City.Create(
            GovernorateId.New(),
            "دمنهور",
            "Damanhur");

        city.Deactivate();

        Assert.False(city.IsActive);

        Assert.True(
            city.UpdatedOnUtc >=
            city.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldMakeCityActive()
    {
        City city = City.Create(
            GovernorateId.New(),
            "دمنهور",
            "Damanhur");

        city.Deactivate();
        city.Activate();

        Assert.True(city.IsActive);

        Assert.True(
            city.UpdatedOnUtc >=
            city.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldDoNothing_WhenAlreadyActive()
    {
        City city = City.Create(
            GovernorateId.New(),
            "دمنهور",
            "Damanhur");

        DateTime updatedOnUtc =
            city.UpdatedOnUtc;

        city.Activate();

        Assert.True(city.IsActive);

        Assert.Equal(
            updatedOnUtc,
            city.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldDoNothing_WhenAlreadyInactive()
    {
        City city = City.Create(
            GovernorateId.New(),
            "دمنهور",
            "Damanhur");

        city.Deactivate();

        DateTime updatedOnUtc =
            city.UpdatedOnUtc;

        city.Deactivate();

        Assert.False(city.IsActive);

        Assert.Equal(
            updatedOnUtc,
            city.UpdatedOnUtc);
    }
}