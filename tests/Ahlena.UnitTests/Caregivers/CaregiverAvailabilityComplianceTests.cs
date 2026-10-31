using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.UnitTests.Caregivers;

public sealed class CaregiverAvailabilityComplianceTests
{
    [Fact]
    public void BecomeAvailable_ShouldRejectOnboardingCaregiver()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldRejectSuspendedCaregiver()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        caregiver.TransitionToSuspended();

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldAllowActiveCompanion()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldRejectRevokedMandatoryCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        CaregiverCertificate practiceLicense =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.RevokeCertificate(
            practiceLicense.Id,
            "Practice License revoked.",
            CaregiverTestData.CurrentUtc);

        // Revocation unseats the effective current version, so the medical
        // caregiver is NOT qualified to become available.
        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Theory]
    [InlineData(CertificateVerificationStatus.Pending)]
    [InlineData(CertificateVerificationStatus.Rejected)]
    public void BecomeAvailable_ShouldHonorApprovedCurrent_WhileReplacementPendingOrRejected(
        CertificateVerificationStatus replacementOutcome)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        CaregiverCertificate practiceLicense =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.UpdateCertificateFile(
            practiceLicense.Id,
            "certificates/new-practice-license.jpg",
            expiryDate: null,
            CreateCurrentDate(),
            CaregiverTestData.CurrentUtc);

        if (replacementOutcome ==
            CertificateVerificationStatus.Rejected)
        {
            caregiver.RejectCertificate(
                practiceLicense.Id,
                "Practice License rejected.");
        }

        // Approved 2026-10-27 contract: the valid approved certificate
        // REMAINS the effective current version while a replacement is
        // pending or even rejected, so availability is not suspended — the
        // pending/rejected candidate never unseats the approved current.
        caregiver.BecomeAvailable(
            CreateCurrentDate());

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            practiceLicense
                .EffectiveSnapshotVersion!
                .VerificationStatus);
    }

    [Fact]
    public void BecomeAvailable_ShouldAllowMedicalWithVerifiedMandatoryCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldRejectExpiredMandatoryCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: currentDate,
            currentDate);

        caregiver.AddCertificate(
            CaregiverCertificateType.GraduationCertificate,
            "certificates/graduation.jpg",
            expiryDate: null,
            currentDate);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                currentDate.AddDays(1)));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldAcceptMandatoryCertificateExpiringToday()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/practice-license.jpg",
            expiryDate: currentDate,
            currentDate);

        caregiver.AddCertificate(
            CaregiverCertificateType.GraduationCertificate,
            "certificates/graduation.jpg",
            expiryDate: currentDate,
            currentDate);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            currentDate);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldIgnoreAdditionalCertificateCompliance()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            CaregiverCertificateType.AdditionalCertificate,
            "certificates/additional.jpg",
            expiryDate: currentDate,
            currentDate);

        CaregiverCertificate additionalCertificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        caregiver.VerifyCertificate(
            additionalCertificate.Id);

        caregiver.RevokeCertificate(
            additionalCertificate.Id,
            "Additional Certificate revoked.",
            CaregiverTestData.CurrentUtc);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            currentDate.AddDays(1));

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    private static CaregiverCertificate GetCertificate(
        Caregiver caregiver,
        CaregiverCertificateType certificateType)
    {
        return caregiver.Certificates.Single(
            certificate =>
                certificate.Type ==
                certificateType);
    }

    private static Caregiver CreateMedicalCaregiver()
    {
        return Caregiver.Create(
            UserId.New(),
            CaregiverType.Medical);
    }

    private static Caregiver CreateCompanionCaregiver()
    {
        return Caregiver.Create(
            UserId.New(),
            CaregiverType.Companion);
    }

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
    }
}