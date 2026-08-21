using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverCertificateTests
{
    [Fact]
    public void AddCertificate_ShouldAddPendingPracticeLicense()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly expiryDate =
            CreateCurrentDate()
                .AddYears(1);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "  certificates/practice-license.jpg  ",
            expiryDate,
            CreateCurrentDate());

        CaregiverCertificate certificate =
            Assert.Single(
                caregiver.Certificates);

        Assert.NotEqual(
            CaregiverCertificateId.Empty,
            certificate.Id);

        Assert.Equal(
            CaregiverCertificateType.PracticeLicense,
            certificate.Type);

        Assert.Equal(
            "certificates/practice-license.jpg",
            certificate.FilePath);

        Assert.Equal(
            expiryDate,
            certificate.ExpiryDate);

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);
    }

    [Fact]
    public void AddCertificate_ShouldAllowFiveAdditionalCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        for (int number = 1;
             number <=
             Caregiver.MaximumAdditionalCertificates;
             number++)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                $"certificates/additional-{number}.jpg",
                expiryDate: null,
                CreateCurrentDate());
        }

        Assert.Equal(
            Caregiver.MaximumAdditionalCertificates,
            caregiver.Certificates.Count);

        Assert.All(
            caregiver.Certificates,
            certificate =>
                Assert.Equal(
                    CaregiverCertificateType.AdditionalCertificate,
                    certificate.Type));
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    [InlineData(CaregiverCertificateType.AdditionalCertificate)]
    public void AddCertificate_ShouldRejectCompanionCaregiver(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateCompanionCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                certificateType,
                "certificates/document.jpg",
                expiryDate: null,
                CreateCurrentDate()));

        Assert.Empty(caregiver.Certificates);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void AddCertificate_ShouldRejectDuplicateMandatoryType(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        AddCertificate(
            caregiver,
            certificateType);

        DateTime updatedOnUtcAfterFirst =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                certificateType,
                "certificates/second.jpg",
                expiryDate: null,
                CreateCurrentDate()));

        Assert.Single(caregiver.Certificates);

        Assert.Equal(
            updatedOnUtcAfterFirst,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void AddCertificate_ShouldRejectSixthAdditionalCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        for (int number = 1;
             number <=
             Caregiver.MaximumAdditionalCertificates;
             number++)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                $"certificates/additional-{number}.jpg",
                expiryDate: null,
                CreateCurrentDate());
        }

        DateTime updatedOnUtcAtMaximum =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                "certificates/additional-6.jpg",
                expiryDate: null,
                CreateCurrentDate()));

        Assert.Equal(
            Caregiver.MaximumAdditionalCertificates,
            caregiver.Certificates.Count);

        Assert.Equal(
            updatedOnUtcAtMaximum,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    [InlineData(CaregiverCertificateType.AdditionalCertificate)]
    public void AddCertificate_ShouldRejectExpiredCertificate(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                certificateType,
                "certificates/document.jpg",
                currentDate.AddDays(-1),
                currentDate));

        Assert.Empty(caregiver.Certificates);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    [InlineData(CaregiverCertificateType.AdditionalCertificate)]
    public void AddCertificate_ShouldAcceptExpiryToday(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            certificateType,
            "certificates/document.jpg",
            currentDate,
            currentDate);

        CaregiverCertificate certificate =
            Assert.Single(
                caregiver.Certificates);

        Assert.Equal(
            currentDate,
            certificate.ExpiryDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddCertificate_ShouldRejectMissingFile(
        string? filePath)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                CaregiverCertificateType.PracticeLicense,
                filePath!,
                expiryDate: null,
                CreateCurrentDate()));

        Assert.Empty(caregiver.Certificates);
    }

    [Fact]
    public void AddCertificate_ShouldRejectInvalidType()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                (CaregiverCertificateType)999,
                "certificates/document.jpg",
                expiryDate: null,
                CreateCurrentDate()));

        Assert.Empty(caregiver.Certificates);
    }

    [Fact]
    public void VerifyCertificate_ShouldVerifyPendingCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);
    }

    [Fact]
    public void VerifyCertificate_ShouldRejectSecondVerification()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        DateTime certificateUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.VerifyCertificate(
                certificate.Id));

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);
    }

    [Fact]
    public void RejectCertificate_ShouldRejectPendingCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.RejectCertificate(
            certificate.Id,
            "  License image is unclear.  ");

        Assert.Equal(
            CertificateVerificationStatus.Rejected,
            certificate.VerificationStatus);

        Assert.Equal(
            "License image is unclear.",
            certificate.ReviewReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectCertificate_ShouldRequireReason(
        string? reason)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        Assert.Throws<DomainException>(
            () => caregiver.RejectCertificate(
                certificate.Id,
                reason!));

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);
    }

    [Fact]
    public void RevokeCertificate_ShouldRevokeVerifiedCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        caregiver.RevokeCertificate(
            certificate.Id,
            "  License approval withdrawn.  ",
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            CertificateVerificationStatus.Revoked,
            certificate.VerificationStatus);

        Assert.Equal(
            "License approval withdrawn.",
            certificate.ReviewReason);
    }

    [Fact]
    public void RevokeCertificate_ShouldRejectPendingCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        Assert.Throws<DomainException>(
            () => caregiver.RevokeCertificate(
                certificate.Id,
                "Invalid Certificate.",
                CaregiverTestData.CurrentUtc));

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RevokeCertificate_ShouldRequireReason(
        string? reason)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        Assert.Throws<DomainException>(
            () => caregiver.RevokeCertificate(
                certificate.Id,
                reason!,
                CaregiverTestData.CurrentUtc));

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);
    }

    [Theory]
    [InlineData(CertificateVerificationStatus.Pending)]
    [InlineData(CertificateVerificationStatus.Rejected)]
    [InlineData(CertificateVerificationStatus.Verified)]
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void UpdateCertificateFile_ShouldReturnAnyStatusToPending(
        CertificateVerificationStatus initialStatus)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        MoveCertificateToStatus(
            caregiver,
            certificate,
            initialStatus);

        CaregiverCertificateId originalId =
            certificate.Id;

        CaregiverCertificateType originalType =
            certificate.Type;

        DateTime originalCreatedOnUtc =
            certificate.CreatedOnUtc;

        DateOnly newExpiryDate =
            CreateCurrentDate()
                .AddYears(1);

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "  certificates/new-document.jpg  ",
            newExpiryDate,
            CreateCurrentDate(),
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            originalId,
            certificate.Id);

        Assert.Equal(
            originalType,
            certificate.Type);

        Assert.Equal(
            originalCreatedOnUtc,
            certificate.CreatedOnUtc);

        Assert.Equal(
            "certificates/new-document.jpg",
            certificate.FilePath);

        Assert.Equal(
            newExpiryDate,
            certificate.ExpiryDate);

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateCertificateFile_ShouldRejectMissingFileWithoutMutation(
        string? filePath)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        string originalFilePath =
            certificate.FilePath;

        DateTime originalUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                certificate.Id,
                filePath!,
                expiryDate: null,
                CreateCurrentDate(),
                CaregiverTestData.CurrentUtc));

        Assert.Equal(
            originalFilePath,
            certificate.FilePath);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Equal(
            originalUpdatedOnUtc,
            certificate.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCertificateFile_ShouldRejectExpiredReplacement()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        DateOnly currentDate =
            CreateCurrentDate();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                certificate.Id,
                "certificates/expired.jpg",
                currentDate.AddDays(-1),
                currentDate,
                CaregiverTestData.CurrentUtc));

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);
    }

    [Theory]
    [InlineData(CertificateVerificationStatus.Pending)]
    [InlineData(CertificateVerificationStatus.Rejected)]
    [InlineData(CertificateVerificationStatus.Verified)]
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void RemoveCertificate_ShouldRemoveAdditionalFromAnyStatus(
        CertificateVerificationStatus initialStatus)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        MoveCertificateToStatus(
            caregiver,
            certificate,
            initialStatus);

        caregiver.RemoveCertificate(
            certificate.Id);

        Assert.DoesNotContain(
            caregiver.Certificates,
            existing =>
                existing.Id ==
                certificate.Id);
    }

    [Fact]
    public void RemoveCertificate_ShouldAllowAdditionalCapacityReuse()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        for (int number = 1;
             number <=
             Caregiver.MaximumAdditionalCertificates;
             number++)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                $"certificates/additional-{number}.jpg",
                expiryDate: null,
                CreateCurrentDate());
        }

        CaregiverCertificate certificateToRemove =
            caregiver.Certificates.First();

        caregiver.RemoveCertificate(
            certificateToRemove.Id);

        caregiver.AddCertificate(
            CaregiverCertificateType.AdditionalCertificate,
            "certificates/replacement.jpg",
            expiryDate: null,
            CreateCurrentDate());

        Assert.Equal(
            Caregiver.MaximumAdditionalCertificates,
            caregiver.Certificates.Count);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void RemoveCertificate_ShouldRejectMandatoryCertificate(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                certificateType);

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificate(
                certificate.Id));

        Assert.Single(caregiver.Certificates);
    }

    [Fact]
    public void CertificateOperations_ShouldRejectEmptyId()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.VerifyCertificate(
                CaregiverCertificateId.Empty));

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                CaregiverCertificateId.Empty,
                "certificates/document.jpg",
                expiryDate: null,
                CreateCurrentDate(),
                CaregiverTestData.CurrentUtc));

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificate(
                CaregiverCertificateId.Empty));
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void RejectCertificate_ShouldKeepPendingReview_WhenActiveMandatoryReplacementRejected(
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
            CreateCurrentDate(),
            CaregiverTestData.CurrentUtc);

        caregiver.RejectCertificate(
            certificate.Id,
            "Invalid mandatory Certificate.");

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Equal(
            CertificateVerificationStatus.Rejected,
            certificate.VerificationStatus);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void RejectCertificate_ShouldKeepActive_WhenAdditional()
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        caregiver.RejectCertificate(
            certificate.Id,
            "Additional Certificate rejected.");

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    private static Caregiver CreateActiveMedicalCaregiver()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CreateCurrentDate());

        return caregiver;
    }

    private static CaregiverCertificate AddCertificate(
        Caregiver caregiver,
        CaregiverCertificateType certificateType)
    {
        caregiver.AddCertificate(
            certificateType,
            "certificates/document.jpg",
            expiryDate: null,
            CreateCurrentDate());

        return caregiver.Certificates.Single(
            certificate =>
                certificate.Type ==
                certificateType);
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

            case CertificateVerificationStatus.Verified:
                caregiver.VerifyCertificate(
                    certificate.Id);
                return;

            case CertificateVerificationStatus.Revoked:
                caregiver.VerifyCertificate(
                    certificate.Id);

                caregiver.RevokeCertificate(
                    certificate.Id,
                    "Certificate revoked.",
                    CaregiverTestData.CurrentUtc);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(status),
                    status,
                    "Unsupported Certificate status.");
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