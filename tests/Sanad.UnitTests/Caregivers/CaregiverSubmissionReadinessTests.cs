using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverSubmissionReadinessTests
{
    [Fact]
    public void ValidateSubmissionReadiness_ShouldAllowReadyCompanion()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        caregiver.ValidateSubmissionReadiness(
            CreateCurrentDate());
    }

    [Fact]
    public void ValidateSubmissionReadiness_ShouldAllowPendingMedicalCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        caregiver.ValidateSubmissionReadiness(
            CreateCurrentDate());
    }

    [Fact]
    public void ValidateSubmissionReadiness_ShouldAllowVerifiedMedicalCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        foreach (CaregiverCertificate certificate
                 in caregiver.Certificates)
        {
            caregiver.VerifyCertificate(
                certificate.Id);
        }

        caregiver.ValidateSubmissionReadiness(
            CreateCurrentDate());
    }

    [Theory]
    [InlineData(MissingSharedSelection.Service)]
    [InlineData(MissingSharedSelection.Language)]
    [InlineData(MissingSharedSelection.Area)]
    public void ValidateSubmissionReadiness_ShouldRejectMissingSharedSelection(
        MissingSharedSelection missingSelection)
    {
        Caregiver caregiver =
            CreateCompanionCaregiver(
                includeService:
                    missingSelection !=
                    MissingSharedSelection.Service,
                includeLanguage:
                    missingSelection !=
                    MissingSharedSelection.Language,
                includeArea:
                    missingSelection !=
                    MissingSharedSelection.Area);

        Assert.Throws<DomainException>(
            () => caregiver
                .ValidateSubmissionReadiness(
                    CreateCurrentDate()));
    }

    [Theory]
    [InlineData(MissingCompanionRequirement.Profile)]
    [InlineData(MissingCompanionRequirement.Pricing)]
    [InlineData(MissingCompanionRequirement.Schedule)]
    public void ValidateSubmissionReadiness_ShouldRejectMissingCompanionRequirement(
        MissingCompanionRequirement missingRequirement)
    {
        Caregiver caregiver =
            CreateCompanionCaregiver(
                includeProfile:
                    missingRequirement !=
                    MissingCompanionRequirement.Profile,
                includePricing:
                    missingRequirement !=
                    MissingCompanionRequirement.Pricing,
                includeSchedule:
                    missingRequirement !=
                    MissingCompanionRequirement.Schedule);

        Assert.Throws<DomainException>(
            () => caregiver
                .ValidateSubmissionReadiness(
                    CreateCurrentDate()));
    }

    [Theory]
    [InlineData(MissingMedicalRequirement.Profile)]
    [InlineData(MissingMedicalRequirement.Pricing)]
    [InlineData(MissingMedicalRequirement.Schedule)]
    [InlineData(MissingMedicalRequirement.PracticeLicense)]
    [InlineData(MissingMedicalRequirement.GraduationCertificate)]
    public void ValidateSubmissionReadiness_ShouldRejectMissingMedicalRequirement(
        MissingMedicalRequirement missingRequirement)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver(
                includeProfile:
                    missingRequirement !=
                    MissingMedicalRequirement.Profile,
                includePricing:
                    missingRequirement !=
                    MissingMedicalRequirement.Pricing,
                includeSchedule:
                    missingRequirement !=
                    MissingMedicalRequirement.Schedule,
                includePracticeLicense:
                    missingRequirement !=
                    MissingMedicalRequirement.PracticeLicense,
                includeGraduationCertificate:
                    missingRequirement !=
                    MissingMedicalRequirement.GraduationCertificate);

        Assert.Throws<DomainException>(
            () => caregiver
                .ValidateSubmissionReadiness(
                    CreateCurrentDate()));
    }

    [Theory]
    [InlineData(CertificateVerificationStatus.Rejected)]
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void ValidateSubmissionReadiness_ShouldRejectInvalidMandatoryCertificateStatus(
        CertificateVerificationStatus certificateStatus)
    {
        Caregiver caregiver =
        CreateMedicalCaregiver();

        CaregiverCertificate practiceLicense =
            caregiver.Certificates.Single(
                certificate =>
                    certificate.Type ==
                    CaregiverCertificateType.PracticeLicense);

        if (certificateStatus ==
            CertificateVerificationStatus.Rejected)
        {
            caregiver.RejectCertificate(
                practiceLicense.Id,
                "Invalid Practice License.");
        }
        else
        {
            caregiver.VerifyCertificate(
                practiceLicense.Id);

            caregiver.RevokeCertificate(
                practiceLicense.Id,
                "Practice License revoked.",
                CaregiverTestData.CurrentUtc);
        }

        Assert.Throws<DomainException>(
            () => caregiver
                .ValidateSubmissionReadiness(
                    CreateCurrentDate()));
    }

    [Fact]
    public void ValidateSubmissionReadiness_ShouldRejectExpiredMandatoryCertificate()
    {
        DateOnly currentDate =
            CreateCurrentDate();

        Caregiver caregiver =
            CreateMedicalCaregiver(
                includePracticeLicense: false);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: currentDate,
            currentDate);

        Assert.Throws<DomainException>(
            () => caregiver
                .ValidateSubmissionReadiness(
                    currentDate.AddDays(1)));
    }

    private static Caregiver CreateCompanionCaregiver(
        bool includeService = true,
        bool includeLanguage = true,
        bool includeArea = true,
        bool includeProfile = true,
        bool includePricing = true,
        bool includeSchedule = true)
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Companion);

        AddSharedSelections(
            caregiver,
            CaregiverType.Companion,
            includeService,
            includeLanguage,
            includeArea);

        if (includeProfile)
        {
            caregiver.UpdateCompanionProfile(
                yearsOfExperience: 5,
                Specialization.Create(
                    "رعاية كبار السن",
                    "Elderly Care",
                    CaregiverType.Companion),
                biography: null);
        }

        if (includePricing)
        {
            caregiver.UpdateCompanionPricing(
                hourlyPrice: 75m,
                eightHourDayPrice: 500m,
                overnightPrice: 650m);
        }

        if (includeSchedule)
        {
            caregiver
                .AddCompanionAvailabilityWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(13, 0));
        }

        return caregiver;
    }

    private static Caregiver CreateMedicalCaregiver(
        bool includeService = true,
        bool includeLanguage = true,
        bool includeArea = true,
        bool includeProfile = true,
        bool includePricing = true,
        bool includeSchedule = true,
        bool includePracticeLicense = true,
        bool includeGraduationCertificate = true)
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        AddSharedSelections(
            caregiver,
            CaregiverType.Medical,
            includeService,
            includeLanguage,
            includeArea);

        if (includeProfile)
        {
            caregiver.UpdateMedicalProfile(
                ProfessionalTitle.Create(
                    "ممرض مسجل",
                    "Registered Nurse"),
                yearsOfExperience: 8,
                Specialization.Create(
                    "تمريض كبار السن",
                    "Elderly Nursing",
                    CaregiverType.Medical),
                AcademicDegree.Create(
                    "بكالوريوس تمريض",
                    "Bachelor of Nursing"),
                currentWorkplace: null,
                biography: null,
                CaregiverTestData.CurrentUtc);
        }

        if (includePricing)
        {
            caregiver.UpdateMedicalPricing(
                homeVisitPrice: 200m,
                eightHourShiftPrice: 600m,
                twelveHourShiftPrice: 850m,
                twentyFourHourShiftPrice: 1500m);
        }

        if (includeSchedule)
        {
            caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning);
        }

        DateOnly currentDate =
            CreateCurrentDate();

        if (includePracticeLicense)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.PracticeLicense,
                "certificates/practice-license.jpg",
                expiryDate: null,
                currentDate);
        }

        if (includeGraduationCertificate)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.GraduationCertificate,
                "certificates/graduation.jpg",
                expiryDate: null,
                currentDate);
        }

        return caregiver;
    }

    private static void AddSharedSelections(
        Caregiver caregiver,
        CaregiverType caregiverType,
        bool includeService,
        bool includeLanguage,
        bool includeArea)
    {
        if (includeService)
        {
            caregiver.SelectService(
                Service.Create(
                    "خدمة رعاية",
                    "Care Service",
                    "icons/care-service.svg",
                    caregiverType,
                    isActive: true));
        }

        if (includeLanguage)
        {
            caregiver.SelectLanguage(
                Language.Create(
                    "ar",
                    "العربية",
                    "Arabic"));
        }

        if (includeArea)
        {
            caregiver.SelectArea(
                Area.Create(
                    CityId.New(),
                    "منطقة خدمة",
                    "Service Area"));
        }
    }

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
    }

    public enum MissingSharedSelection
    {
        Service = 1,
        Language = 2,
        Area = 3
    }

    public enum MissingCompanionRequirement
    {
        Profile = 1,
        Pricing = 2,
        Schedule = 3
    }

    public enum MissingMedicalRequirement
    {
        Profile = 1,
        Pricing = 2,
        Schedule = 3,
        PracticeLicense = 4,
        GraduationCertificate = 5
    }
}