using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

internal static class CaregiverTestData
{
    internal static readonly DateOnly CurrentDate =
        new(
            2026,
            8,
            20);

    internal static void EnsureReadyForSubmission(
        Caregiver caregiver)
    {
        EnsureSharedSelections(
            caregiver);

        switch (caregiver.Type)
        {
            case CaregiverType.Medical:
                EnsureMedicalReadiness(
                    caregiver);
                return;

            case CaregiverType.Companion:
                EnsureCompanionReadiness(
                    caregiver);
                return;

            default:
                throw new InvalidOperationException(
                    "Unsupported Caregiver type.");
        }
    }

    private static void EnsureSharedSelections(
        Caregiver caregiver)
    {
        if (caregiver.ServiceSelections.Count == 0)
        {
            caregiver.SelectService(
                Service.Create(
                    "خدمة رعاية",
                    "Care Service",
                    "icons/care-service.svg",
                    caregiver.Type,
                    isActive: true));
        }

        if (caregiver.LanguageSelections.Count == 0)
        {
            caregiver.SelectLanguage(
                Language.Create(
                    "ar",
                    "العربية",
                    "Arabic"));
        }

        if (caregiver.AreaSelections.Count == 0)
        {
            caregiver.SelectArea(
                Area.Create(
                    CityId.New(),
                    "منطقة خدمة",
                    "Service Area"));
        }
    }

    private static void EnsureCompanionReadiness(
        Caregiver caregiver)
    {
        if (caregiver.CompanionProfile is null)
        {
            caregiver.UpdateCompanionProfile(
                yearsOfExperience: 5,
                Specialization.Create(
                    "رعاية كبار السن",
                    "Elderly Care",
                    CaregiverType.Companion),
                biography: null);
        }

        if (caregiver.CompanionPricing is null)
        {
            caregiver.UpdateCompanionPricing(
                hourlyPrice: 75m,
                eightHourDayPrice: 500m,
                overnightPrice: 650m);
        }

        if (caregiver.CompanionSchedule is null ||
            !caregiver.CompanionSchedule.HasAvailability)
        {
            caregiver
                .AddCompanionAvailabilityWindow(
                    CompanionBookingType.Hourly,
                    DayOfWeek.Saturday,
                    new TimeOnly(9, 0),
                    new TimeOnly(13, 0));
        }
    }

    private static void EnsureMedicalReadiness(
        Caregiver caregiver)
    {
        if (caregiver.MedicalProfile is null)
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
                biography: null);
        }

        if (caregiver.MedicalPricing is null)
        {
            caregiver.UpdateMedicalPricing(
                homeVisitPrice: 200m,
                eightHourShiftPrice: 600m,
                twelveHourShiftPrice: 850m,
                twentyFourHourShiftPrice: 1500m);
        }

        if (caregiver.MedicalSchedule is null ||
            !caregiver.MedicalSchedule.HasAvailability)
        {
            caregiver.AddMedicalShift(
                DayOfWeek.Saturday,
                MedicalShiftType.EightHourMorning);
        }

        EnsureMandatoryCertificate(
            caregiver,
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg");

        EnsureMandatoryCertificate(
            caregiver,
            CaregiverCertificateType.GraduationCertificate,
            "certificates/graduation.jpg");
    }

    private static void EnsureMandatoryCertificate(
        Caregiver caregiver,
        CaregiverCertificateType certificateType,
        string filePath)
    {
        bool alreadyExists =
            caregiver.Certificates.Any(
                certificate =>
                    certificate.Type ==
                    certificateType);

        if (alreadyExists)
        {
            return;
        }

        caregiver.AddCertificate(
            certificateType,
            filePath,
            expiryDate: null,
            CurrentDate);
    }
}