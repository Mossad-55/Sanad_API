using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverCertificateStatusConsequencesTests
{
    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void UpdateCertificateFile_ShouldReturnActiveCaregiverToPendingReview_WhenMandatory(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                certificateType);

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/replacement.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate,
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

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

    [Fact]
    public void UpdateCertificateFile_ShouldKeepOnboardingStatus_WhenMandatory()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "certificates/original.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate);

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/replacement.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate,
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);
    }

    [Fact]
    public void UpdateCertificateFile_ShouldKeepActiveStatus_WhenAdditional()
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            AddAdditionalCertificate(
                caregiver);

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/new-additional.jpg",
            expiryDate: null,
            CaregiverTestData.CurrentDate,
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void RevokeCertificate_ShouldSuspendActiveCaregiver_WhenMandatory(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                certificateType);

        caregiver.RevokeCertificate(
            certificate.Id,
            "  Approval withdrawn.  ",
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CertificateVerificationStatus.Revoked,
            certificate.VerificationStatus);

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);

        Assert.Equal(
            "Approval withdrawn.",
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            CaregiverTestData.CurrentUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RevokeCertificate_ShouldKeepActiveStatus_WhenAdditional()
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            AddAdditionalCertificate(
                caregiver);

        caregiver.VerifyCertificate(
            certificate.Id);

        caregiver.RevokeCertificate(
            certificate.Id,
            "Additional Certificate revoked.",
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Null(caregiver.StatusReason);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void SuspendForExpiredMandatoryCertificate_ShouldSuspendActiveCaregiver(
        CaregiverCertificateType certificateType)
    {
        DateOnly currentDate =
            CaregiverTestData.CurrentDate;

        Caregiver caregiver =
            CreateActiveMedicalCaregiverWithExpiry(
                certificateType,
                currentDate);

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                certificateType);

        DateTime suspendedOnUtc =
            CaregiverTestData.CurrentUtc
                .AddDays(1);

        caregiver.SuspendForExpiredMandatoryCertificate(
            certificate.Id,
            currentDate.AddDays(1),
            suspendedOnUtc);

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);

        Assert.Equal(
            $"{certificateType} has expired.",
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            suspendedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SuspendForExpiredMandatoryCertificate_ShouldRejectNonExpiredCertificate()
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver
                .SuspendForExpiredMandatoryCertificate(
                    certificate.Id,
                    CaregiverTestData.CurrentDate,
                    CaregiverTestData.CurrentUtc));

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
    public void SuspendForExpiredMandatoryCertificate_ShouldRejectAdditionalCertificate()
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            AddAdditionalCertificate(
                caregiver,
                CaregiverTestData.CurrentDate);

        Assert.Throws<DomainException>(
            () => caregiver
                .SuspendForExpiredMandatoryCertificate(
                    certificate.Id,
                    CaregiverTestData.CurrentDate
                        .AddDays(1),
                    CaregiverTestData.CurrentUtc));

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void UpdateCertificateFile_ShouldRejectNonUtcTimeWithoutMutation(
        DateTimeKind dateTimeKind)
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        string originalFilePath =
            certificate.FilePath;

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        DateTime invalidTime =
            DateTime.SpecifyKind(
                CaregiverTestData.CurrentUtc,
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                certificate.Id,
                "certificates/replacement.jpg",
                expiryDate: null,
                CaregiverTestData.CurrentDate,
                invalidTime));

        Assert.Equal(
            originalFilePath,
            certificate.FilePath);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

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

    private static Caregiver CreateActiveMedicalCaregiver()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CaregiverTestData.CurrentDate);

        return caregiver;
    }

    private static Caregiver
        CreateActiveMedicalCaregiverWithExpiry(
            CaregiverCertificateType certificateType,
            DateOnly expiryDate)
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.AddCertificate(
            certificateType,
            "certificates/expiring-document.jpg",
            expiryDate,
            CaregiverTestData.CurrentDate);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CaregiverTestData.CurrentDate);

        return caregiver;
    }

    private static CaregiverCertificate AddAdditionalCertificate(
        Caregiver caregiver,
        DateOnly? expiryDate = null)
    {
        caregiver.AddCertificate(
            CaregiverCertificateType.AdditionalCertificate,
            "certificates/additional.jpg",
            expiryDate,
            CaregiverTestData.CurrentDate);

        return GetCertificate(
            caregiver,
            CaregiverCertificateType.AdditionalCertificate);
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
}