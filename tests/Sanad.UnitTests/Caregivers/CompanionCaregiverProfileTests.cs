using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CompanionCaregiverProfileTests
{
    [Fact]
    public void Create_ShouldStoreProfessionalInformation()
    {
        SpecializationId specializationId =
            SpecializationId.New();

        CompanionCaregiverProfile profile =
            CompanionCaregiverProfile.Create(
                yearsOfExperience: 5,
                specializationId,
                "  Experienced elderly companion.  ");

        Assert.Equal(
            5,
            profile.YearsOfExperience);

        Assert.Equal(
            specializationId,
            profile.SpecializationId);

        Assert.Equal(
            "Experienced elderly companion.",
            profile.Biography);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldNormalizeEmptyBiographyToNull(
        string? biography)
    {
        CompanionCaregiverProfile profile =
            CompanionCaregiverProfile.Create(
                yearsOfExperience: 5,
                SpecializationId.New(),
                biography);

        Assert.Null(profile.Biography);
    }

    [Fact]
    public void Create_ShouldRejectEmptySpecializationId()
    {
        Assert.Throws<DomainException>(
            () => CompanionCaregiverProfile.Create(
                yearsOfExperience: 5,
                SpecializationId.Empty,
                biography: null));
    }

    [Fact]
    public void Create_ShouldRejectNegativeExperience()
    {
        Assert.Throws<DomainException>(
            () => CompanionCaregiverProfile.Create(
                yearsOfExperience: -1,
                SpecializationId.New(),
                biography: null));
    }

    [Fact]
    public void Create_ShouldRejectBiographyThatIsTooLong()
    {
        string longBiography = new(
            'A',
            CompanionCaregiverProfile
                .MaximumBiographyLength + 1);

        Assert.Throws<DomainException>(
            () => CompanionCaregiverProfile.Create(
                yearsOfExperience: 5,
                SpecializationId.New(),
                longBiography));
    }
}