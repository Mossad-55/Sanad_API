using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverProfessionalProfileTests
{
    [Fact]
    public void UpdateMedicalProfile_ShouldStoreMedicalProfile()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        ProfessionalTitle title =
            CreateProfessionalTitle();

        Specialization specialization =
            CreateSpecialization(
                CaregiverType.Medical);

        AcademicDegree degree =
            CreateAcademicDegree();

        caregiver.UpdateMedicalProfile(
            title,
            yearsOfExperience: 8,
            specialization,
            degree,
            "  Al Salam Hospital  ",
            "  Experienced Medical caregiver.  ",
            CaregiverTestData.CurrentUtc);

        Assert.NotNull(caregiver.MedicalProfile);
        Assert.Null(caregiver.CompanionProfile);

        Assert.Equal(
            title.Id,
            caregiver.MedicalProfile
                .ProfessionalTitleId);

        Assert.Equal(
            specialization.Id,
            caregiver.MedicalProfile
                .SpecializationId);

        Assert.Equal(
            degree.Id,
            caregiver.MedicalProfile
                .AcademicDegreeId);

        Assert.Equal(
            8,
            caregiver.MedicalProfile
                .YearsOfExperience);

        Assert.Equal(
            "Al Salam Hospital",
            caregiver.MedicalProfile
                .CurrentWorkplace);

        Assert.Equal(
            "Experienced Medical caregiver.",
            caregiver.MedicalProfile
                .Biography);
    }

    [Fact]
    public void UpdateCompanionProfile_ShouldStoreCompanionProfile()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Specialization specialization =
            CreateSpecialization(
                CaregiverType.Companion);

        caregiver.UpdateCompanionProfile(
            yearsOfExperience: 5,
            specialization,
            "  Experienced Companion caregiver.  ");

        Assert.NotNull(caregiver.CompanionProfile);
        Assert.Null(caregiver.MedicalProfile);

        Assert.Equal(
            specialization.Id,
            caregiver.CompanionProfile
                .SpecializationId);

        Assert.Equal(
            5,
            caregiver.CompanionProfile
                .YearsOfExperience);

        Assert.Equal(
            "Experienced Companion caregiver.",
            caregiver.CompanionProfile
                .Biography);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldRejectCompanionCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                5,
                CreateSpecialization(
                    CaregiverType.Medical),
                CreateAcademicDegree(),
                null,
                null,
                CaregiverTestData.CurrentUtc));

        Assert.Null(caregiver.MedicalProfile);
        Assert.Null(caregiver.CompanionProfile);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCompanionProfile_ShouldRejectMedicalCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCompanionProfile(
                5,
                CreateSpecialization(
                    CaregiverType.Companion),
                null));

        Assert.Null(caregiver.MedicalProfile);
        Assert.Null(caregiver.CompanionProfile);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldRejectInactiveProfessionalTitle()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        ProfessionalTitle title =
            CreateProfessionalTitle();

        title.Deactivate();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                title,
                5,
                CreateSpecialization(
                    CaregiverType.Medical),
                CreateAcademicDegree(),
                null,
                null,
                CaregiverTestData.CurrentUtc));

        Assert.Null(caregiver.MedicalProfile);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldRejectInactiveSpecialization()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Specialization specialization =
            CreateSpecialization(
                CaregiverType.Medical);

        specialization.Deactivate();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                5,
                specialization,
                CreateAcademicDegree(),
                null,
                null,
                CaregiverTestData.CurrentUtc));

        Assert.Null(caregiver.MedicalProfile);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldRejectInactiveAcademicDegree()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        AcademicDegree degree =
            CreateAcademicDegree();

        degree.Deactivate();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                5,
                CreateSpecialization(
                    CaregiverType.Medical),
                degree,
                null,
                null,
                CaregiverTestData.CurrentUtc));

        Assert.Null(caregiver.MedicalProfile);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldRejectCompanionSpecialization()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                5,
                CreateSpecialization(
                    CaregiverType.Companion),
                CreateAcademicDegree(),
                null,
                null,
                CaregiverTestData.CurrentUtc));

        Assert.Null(caregiver.MedicalProfile);
    }

    [Fact]
    public void UpdateCompanionProfile_ShouldRejectMedicalSpecialization()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCompanionProfile(
                5,
                CreateSpecialization(
                    CaregiverType.Medical),
                null));

        Assert.Null(caregiver.CompanionProfile);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldBeAtomic_WhenValidationFails()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.UpdateMedicalProfile(
            CreateProfessionalTitle(),
            5,
            CreateSpecialization(
                CaregiverType.Medical),
            CreateAcademicDegree(),
            null,
            "Original biography.",
            CaregiverTestData.CurrentUtc);

        MedicalCaregiverProfile originalProfile =
            caregiver.MedicalProfile!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                -1,
                CreateSpecialization(
                    CaregiverType.Medical),
                CreateAcademicDegree(),
                null,
                "New biography.",
                CaregiverTestData.CurrentUtc));

        Assert.Same(
            originalProfile,
            caregiver.MedicalProfile);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldReturnActiveCaregiverToPendingReview()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.UpdateMedicalProfile(
            CreateProfessionalTitle(),
            5,
            CreateSpecialization(
                CaregiverType.Medical),
            CreateAcademicDegree(),
            null,
            null,
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            CaregiverTestData.CurrentUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void UpdateMedicalProfile_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime invalidTime =
            DateTime.SpecifyKind(
                new DateTime(
                    2026,
                    8,
                    20,
                    10,
                    0,
                    0),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                5,
                CreateSpecialization(
                    CaregiverType.Medical),
                CreateAcademicDegree(),
                null,
                null,
                invalidTime));

        Assert.Null(caregiver.MedicalProfile);

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);
    }

    [Fact]
    public void UpdateMedicalProfile_ShouldKeepActiveState_WhenValidationFails()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        MedicalCaregiverProfile originalProfile =
            caregiver.MedicalProfile!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalProfile(
                CreateProfessionalTitle(),
                -1,
                CreateSpecialization(
                    CaregiverType.Medical),
                CreateAcademicDegree(),
                null,
                null,
                CaregiverTestData.CurrentUtc));

        Assert.Same(
            originalProfile,
            caregiver.MedicalProfile);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCompanionProfile_ShouldKeepActiveCaregiverAvailable()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        caregiver.UpdateCompanionProfile(
            5,
            CreateSpecialization(
                CaregiverType.Companion),
            null);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    private static void MakeMedicalCaregiverCompliantAndAvailable(
        Caregiver caregiver)
    {
        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: null,
            currentDate);

        caregiver.AddCertificate(
            CaregiverCertificateType.GraduationCertificate,
            "certificates/graduation.jpg",
            expiryDate: null,
            currentDate);

        foreach (CaregiverCertificate certificate
                 in caregiver.Certificates)
        {
            caregiver.VerifyCertificate(
                certificate.Id);
        }

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            currentDate);
    }

    private static Caregiver CreateCaregiver(
        CaregiverType caregiverType)
    {
        return Caregiver.Create(
            UserId.New(),
            caregiverType);
    }

    private static ProfessionalTitle CreateProfessionalTitle()
    {
        return ProfessionalTitle.Create(
            "ممرض مسجل",
            "Registered Nurse",
            true);
    }

    private static Specialization CreateSpecialization(
        CaregiverType caregiverType)
    {
        return Specialization.Create(
            "رعاية كبار السن",
            "Elderly Care",
            true,
            caregiverType);
    }

    private static AcademicDegree CreateAcademicDegree()
    {
        return AcademicDegree.Create(
            "بكالوريوس تمريض",
            "Bachelor of Nursing",
            true);
    }

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
    }
}