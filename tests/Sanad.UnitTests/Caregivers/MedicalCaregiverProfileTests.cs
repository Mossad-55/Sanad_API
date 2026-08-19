using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class MedicalCaregiverProfileTests
{
    [Fact]
    public void Create_ShouldStoreProfessionalInformation()
    {
        ProfessionalTitleId titleId =
            ProfessionalTitleId.New();

        SpecializationId specializationId =
            SpecializationId.New();

        AcademicDegreeId degreeId =
            AcademicDegreeId.New();

        MedicalCaregiverProfile profile =
            MedicalCaregiverProfile.Create(
                titleId,
                yearsOfExperience: 8,
                specializationId,
                degreeId,
                "  Al Salam Hospital  ",
                "  Experienced elderly-care nurse.  ");

        Assert.Equal(
            titleId,
            profile.ProfessionalTitleId);

        Assert.Equal(
            8,
            profile.YearsOfExperience);

        Assert.Equal(
            specializationId,
            profile.SpecializationId);

        Assert.Equal(
            degreeId,
            profile.AcademicDegreeId);

        Assert.Equal(
            "Al Salam Hospital",
            profile.CurrentWorkplace);

        Assert.Equal(
            "Experienced elderly-care nurse.",
            profile.Biography);
    }

    [Fact]
    public void Create_ShouldNormalizeEmptyOptionalTextToNull()
    {
        MedicalCaregiverProfile profile =
            CreateProfile(
                currentWorkplace: "   ",
                biography: null);

        Assert.Null(profile.CurrentWorkplace);
        Assert.Null(profile.Biography);
    }

    [Fact]
    public void Create_ShouldRejectEmptyProfessionalTitleId()
    {
        Assert.Throws<DomainException>(
            () => MedicalCaregiverProfile.Create(
                ProfessionalTitleId.Empty,
                5,
                SpecializationId.New(),
                AcademicDegreeId.New(),
                null,
                null));
    }

    [Fact]
    public void Create_ShouldRejectEmptySpecializationId()
    {
        Assert.Throws<DomainException>(
            () => MedicalCaregiverProfile.Create(
                ProfessionalTitleId.New(),
                5,
                SpecializationId.Empty,
                AcademicDegreeId.New(),
                null,
                null));
    }

    [Fact]
    public void Create_ShouldRejectEmptyAcademicDegreeId()
    {
        Assert.Throws<DomainException>(
            () => MedicalCaregiverProfile.Create(
                ProfessionalTitleId.New(),
                5,
                SpecializationId.New(),
                AcademicDegreeId.Empty,
                null,
                null));
    }

    [Fact]
    public void Create_ShouldRejectNegativeExperience()
    {
        Assert.Throws<DomainException>(
            () => MedicalCaregiverProfile.Create(
                ProfessionalTitleId.New(),
                -1,
                SpecializationId.New(),
                AcademicDegreeId.New(),
                null,
                null));
    }

    [Fact]
    public void Create_ShouldRejectBiographyThatIsTooLong()
    {
        string longBiography = new(
            'A',
            MedicalCaregiverProfile
                .MaximumBiographyLength + 1);

        Assert.Throws<DomainException>(
            () => CreateProfile(
                currentWorkplace: null,
                biography: longBiography));
    }

    [Fact]
    public void Create_ShouldRejectWorkplaceThatIsTooLong()
    {
        string longWorkplace = new(
            'A',
            MedicalCaregiverProfile
                .MaximumWorkplaceLength + 1);

        Assert.Throws<DomainException>(
            () => CreateProfile(
                currentWorkplace: longWorkplace,
                biography: null));
    }

    private static MedicalCaregiverProfile CreateProfile(
        string? currentWorkplace,
        string? biography)
    {
        return MedicalCaregiverProfile.Create(
            ProfessionalTitleId.New(),
            yearsOfExperience: 5,
            SpecializationId.New(),
            AcademicDegreeId.New(),
            currentWorkplace,
            biography);
    }
}