using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverAvailabilityComplianceTests
{
    [Fact]
    public void BecomeAvailable_ShouldRejectPendingCaregiver()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void BecomeAvailable_ShouldRejectSuspendedCaregiver()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        caregiver.Suspend();

        DateTime updatedOnUtcAfterSuspension =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            updatedOnUtcAfterSuspension,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void BecomeAvailable_ShouldAllowActiveCompanionWithoutCertificates()
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        caregiver.Activate();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldRejectMedicalWithoutCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        caregiver.Activate();

        DateTime updatedOnUtcBeforeAttempt =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            updatedOnUtcBeforeAttempt,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void BecomeAvailable_ShouldRejectMedicalWithMissingMandatoryCertificate(
        CaregiverCertificateType missingCertificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificateType existingCertificateType =
            missingCertificateType ==
            CaregiverCertificateType.PracticeLicense
                ? CaregiverCertificateType.GraduationCertificate
                : CaregiverCertificateType.PracticeLicense;

        CaregiverCertificate existingCertificate =
            AddCertificate(
                caregiver,
                existingCertificateType);

        caregiver.VerifyCertificate(
            existingCertificate.Id);

        caregiver.Activate();

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
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void BecomeAvailable_ShouldRejectNonVerifiedMandatoryCertificate(
        CertificateVerificationStatus certificateStatus)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate practiceLicense =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        CaregiverCertificate graduationCertificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.GraduationCertificate);

        caregiver.VerifyCertificate(
            graduationCertificate.Id);

        MoveCertificateToStatus(
            caregiver,
            practiceLicense,
            certificateStatus);

        caregiver.Activate();

        Assert.Throws<DomainException>(
            () => caregiver.BecomeAvailable(
                CreateCurrentDate()));

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void BecomeAvailable_ShouldAllowMedicalWithVerifiedMandatoryCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        AddAndVerifyMandatoryCertificates(
            caregiver);

        caregiver.Activate();

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

        CaregiverCertificate practiceLicense =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense,
                expiryDate: currentDate);

        CaregiverCertificate graduationCertificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.GraduationCertificate);

        caregiver.VerifyCertificate(
            practiceLicense.Id);

        caregiver.VerifyCertificate(
            graduationCertificate.Id);

        caregiver.Activate();

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

        CaregiverCertificate practiceLicense =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense,
                expiryDate: currentDate);

        CaregiverCertificate graduationCertificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.GraduationCertificate,
                expiryDate: currentDate);

        caregiver.VerifyCertificate(
            practiceLicense.Id);

        caregiver.VerifyCertificate(
            graduationCertificate.Id);

        caregiver.Activate();

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

        AddAndVerifyMandatoryCertificates(
            caregiver);

        DateOnly currentDate =
            CreateCurrentDate();

        CaregiverCertificate additionalCertificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate,
                expiryDate: currentDate);

        caregiver.VerifyCertificate(
            additionalCertificate.Id);

        caregiver.RevokeCertificate(
            additionalCertificate.Id,
            "Additional Certificate revoked.");

        caregiver.Activate();

        caregiver.BecomeAvailable(
            currentDate.AddDays(1));

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    private static void AddAndVerifyMandatoryCertificates(
        Caregiver caregiver)
    {
        CaregiverCertificate practiceLicense =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        CaregiverCertificate graduationCertificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.GraduationCertificate);

        caregiver.VerifyCertificate(
            practiceLicense.Id);

        caregiver.VerifyCertificate(
            graduationCertificate.Id);
    }

    private static CaregiverCertificate AddCertificate(
        Caregiver caregiver,
        CaregiverCertificateType certificateType,
        DateOnly? expiryDate = null)
    {
        caregiver.AddCertificate(
            certificateType,
            "certificates/document.jpg",
            expiryDate,
            CreateCurrentDate());

        return caregiver.Certificates.Single(
            certificate =>
                certificate.Type ==
                certificateType);
    }

    private static void MoveCertificateToStatus(
        Caregiver caregiver,
        CaregiverCertificate certificate,
        CertificateVerificationStatus status)
    {
        switch (status)
        {
            case CertificateVerificationStatus.Pending:
                return;

            case CertificateVerificationStatus.Rejected:
                caregiver.RejectCertificate(
                    certificate.Id,
                    "Certificate rejected.");
                return;

            case CertificateVerificationStatus.Revoked:
                caregiver.VerifyCertificate(
                    certificate.Id);

                caregiver.RevokeCertificate(
                    certificate.Id,
                    "Certificate revoked.");
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(status),
                    status,
                    "Unsupported test status.");
        }
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