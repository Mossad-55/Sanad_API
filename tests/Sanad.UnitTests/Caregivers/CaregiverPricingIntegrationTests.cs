using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverPricingIntegrationTests
{
    [Fact]
    public void UpdateMedicalPricing_ShouldStoreMedicalPricing()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.UpdateMedicalPricing(
            200m,
            600m,
            850m,
            1500m);

        Assert.NotNull(caregiver.MedicalPricing);
        Assert.Null(caregiver.CompanionPricing);

        Assert.Equal(
            200m,
            caregiver.MedicalPricing.HomeVisitPrice);

        Assert.Equal(
            600m,
            caregiver.MedicalPricing
                .EightHourShiftPrice);

        Assert.Equal(
            850m,
            caregiver.MedicalPricing
                .TwelveHourShiftPrice);

        Assert.Equal(
            1500m,
            caregiver.MedicalPricing
                .TwentyFourHourShiftPrice);
    }

    [Fact]
    public void UpdateCompanionPricing_ShouldStoreCompanionPricing()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.UpdateCompanionPricing(
            75m,
            500m,
            650m);

        Assert.NotNull(caregiver.CompanionPricing);
        Assert.Null(caregiver.MedicalPricing);

        Assert.Equal(
            75m,
            caregiver.CompanionPricing.HourlyPrice);

        Assert.Equal(
            500m,
            caregiver.CompanionPricing
                .EightHourDayPrice);

        Assert.Equal(
            650m,
            caregiver.CompanionPricing
                .OvernightPrice);
    }

    [Fact]
    public void UpdateMedicalPricing_ShouldRejectCompanionCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalPricing(
                200m,
                600m,
                850m,
                1500m));

        Assert.Null(caregiver.MedicalPricing);
        Assert.Null(caregiver.CompanionPricing);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCompanionPricing_ShouldRejectMedicalCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCompanionPricing(
                75m,
                500m,
                650m));

        Assert.Null(caregiver.MedicalPricing);
        Assert.Null(caregiver.CompanionPricing);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateMedicalPricing_ShouldBeAtomic_WhenPriceIsInvalid()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.UpdateMedicalPricing(
            200m,
            600m,
            850m,
            1500m);

        MedicalCaregiverPricing originalPricing =
            caregiver.MedicalPricing!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateMedicalPricing(
                250m,
                0m,
                900m,
                1600m));

        Assert.Same(
            originalPricing,
            caregiver.MedicalPricing);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCompanionPricing_ShouldBeAtomic_WhenPriceIsInvalid()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.UpdateCompanionPricing(
            75m,
            500m,
            650m);

        CompanionCaregiverPricing originalPricing =
            caregiver.CompanionPricing!;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCompanionPricing(
                80m,
                550m,
                0m));

        Assert.Same(
            originalPricing,
            caregiver.CompanionPricing);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateMedicalPricing_ShouldKeepActiveCaregiverAvailable()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.UpdateMedicalPricing(
            250m,
            650m,
            900m,
            1600m);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void UpdateCompanionPricing_ShouldKeepActiveCaregiverAvailable()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        caregiver.UpdateCompanionPricing(
            80m,
            550m,
            700m);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void UpdateMedicalPricing_ShouldReplacePreviousPricing()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        caregiver.UpdateMedicalPricing(
            200m,
            600m,
            850m,
            1500m);

        MedicalCaregiverPricing originalPricing =
            caregiver.MedicalPricing!;

        caregiver.UpdateMedicalPricing(
            250m,
            650m,
            900m,
            1600m);

        Assert.NotSame(
            originalPricing,
            caregiver.MedicalPricing);

        Assert.Equal(
            250m,
            caregiver.MedicalPricing!
                .HomeVisitPrice);
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

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
    }
}