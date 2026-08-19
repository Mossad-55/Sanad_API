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

        DateOnly currentDate =
            CreateCurrentDate();

        DateOnly expiryDate =
            currentDate.AddYears(1);

        caregiver.AddCertificate(
            CaregiverCertificateType.PracticeLicense,
            "  certificates/practice-license.jpg  ",
            expiryDate,
            currentDate);

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

        Assert.Equal(
            certificate.CreatedOnUtc,
            certificate.UpdatedOnUtc);
    }

    [Fact]
    public void AddCertificate_ShouldAddGraduationCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        caregiver.AddCertificate(
            CaregiverCertificateType.GraduationCertificate,
            "certificates/graduation.jpg",
            expiryDate: null,
            CreateCurrentDate());

        CaregiverCertificate certificate =
            Assert.Single(
                caregiver.Certificates);

        Assert.Equal(
            CaregiverCertificateType.GraduationCertificate,
            certificate.Type);

        Assert.Null(certificate.ExpiryDate);
    }

    [Fact]
    public void AddCertificate_ShouldAllowFiveAdditionalCertificates()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        for (int certificateNumber = 1;
             certificateNumber <=
             Caregiver.MaximumAdditionalCertificates;
             certificateNumber++)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                $"certificates/additional-{certificateNumber}.jpg",
                expiryDate: null,
                currentDate);
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
    public void AddCertificate_ShouldRejectCertificateForCompanionCaregiver(
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
    public void AddCertificate_ShouldRejectDuplicateMandatoryCertificate(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.AddCertificate(
            certificateType,
            "certificates/first.jpg",
            expiryDate: null,
            currentDate);

        DateTime updatedOnUtcAfterFirstCertificate =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                certificateType,
                "certificates/second.jpg",
                expiryDate: null,
                currentDate));

        CaregiverCertificate certificate =
            Assert.Single(
                caregiver.Certificates);

        Assert.Equal(
            "certificates/first.jpg",
            certificate.FilePath);

        Assert.Equal(
            updatedOnUtcAfterFirstCertificate,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void AddCertificate_ShouldRejectSixthAdditionalCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        for (int certificateNumber = 1;
             certificateNumber <=
             Caregiver.MaximumAdditionalCertificates;
             certificateNumber++)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                $"certificates/additional-{certificateNumber}.jpg",
                expiryDate: null,
                currentDate);
        }

        DateTime updatedOnUtcAtMaximum =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                "certificates/additional-6.jpg",
                expiryDate: null,
                currentDate));

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

        DateOnly expiredDate =
            currentDate.AddDays(-1);

        Assert.Throws<DomainException>(
            () => caregiver.AddCertificate(
                certificateType,
                "certificates/document.jpg",
                expiredDate,
                currentDate));

        Assert.Empty(caregiver.Certificates);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    [InlineData(CaregiverCertificateType.AdditionalCertificate)]
    public void AddCertificate_ShouldAcceptCertificateExpiringToday(
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
    public void AddCertificate_ShouldRejectInvalidCertificateType()
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
    public void VerifyCertificate_ShouldRejectNonPendingCertificate()
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

        DateTime caregiverUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.VerifyCertificate(
                certificate.Id));

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);

        Assert.Equal(
            caregiverUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
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

        DateTime certificateUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        DateTime caregiverUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RejectCertificate(
                certificate.Id,
                reason!));

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);

        Assert.Equal(
            caregiverUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RejectCertificate_ShouldRejectNonPendingCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.RejectCertificate(
            certificate.Id,
            "Invalid document.");

        Assert.Throws<DomainException>(
            () => caregiver.RejectCertificate(
                certificate.Id,
                "Another reason."));

        Assert.Equal(
            CertificateVerificationStatus.Rejected,
            certificate.VerificationStatus);

        Assert.Equal(
            "Invalid document.",
            certificate.ReviewReason);
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
            "  License authority withdrew approval.  ");

        Assert.Equal(
            CertificateVerificationStatus.Revoked,
            certificate.VerificationStatus);

        Assert.Equal(
            "License authority withdrew approval.",
            certificate.ReviewReason);
    }

    [Fact]
    public void RevokeCertificate_ShouldRejectNonVerifiedCertificate()
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
                "Invalid license."));

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);
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

        DateTime certificateUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        DateTime caregiverUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RevokeCertificate(
                certificate.Id,
                reason!));

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);

        Assert.Equal(
            caregiverUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void RejectCertificate_ShouldMakeCaregiverUnavailable_WhenMandatory(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                certificateType);

        caregiver.Activate();

        caregiver.RejectCertificate(
            certificate.Id,
            "Invalid mandatory Certificate.");

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void RevokeCertificate_ShouldMakeCaregiverUnavailable_WhenMandatory(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                certificateType);

        caregiver.VerifyCertificate(
            certificate.Id);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.RevokeCertificate(
            certificate.Id,
            "Approval withdrawn.");

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void RejectCertificate_ShouldKeepCaregiverAvailable_WhenAdditional()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.RejectCertificate(
            certificate.Id,
            "Additional Certificate was not accepted.");

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
    }

    [Fact]
    public void VerifyCertificate_ShouldRejectEmptyCertificateId()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.VerifyCertificate(
                CaregiverCertificateId.Empty));
    }

    [Fact]
    public void VerifyCertificate_ShouldRejectCertificateFromAnotherCaregiver()
    {
        Caregiver firstCaregiver =
            CreateMedicalCaregiver();

        Caregiver secondCaregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                firstCaregiver,
                CaregiverCertificateType.PracticeLicense);

        Assert.Throws<DomainException>(
            () => secondCaregiver.VerifyCertificate(
                certificate.Id));

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);
    }

    [Theory]
    [InlineData(CertificateVerificationStatus.Pending)]
    [InlineData(CertificateVerificationStatus.Rejected)]
    [InlineData(CertificateVerificationStatus.Verified)]
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void UpdateCertificateFile_ShouldReturnCertificateToPending(
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

        DateOnly currentDate =
            CreateCurrentDate();

        DateOnly newExpiryDate =
            currentDate.AddYears(1);

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "  certificates/new-document.jpg  ",
            newExpiryDate,
            currentDate);

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
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void UpdateCertificateFile_ShouldMakeCaregiverUnavailable_WhenMandatory(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                certificateType);

        caregiver.VerifyCertificate(
            certificate.Id);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        DateOnly currentDate =
            CreateCurrentDate();

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/replacement.jpg",
            expiryDate: null,
            currentDate);

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void UpdateCertificateFile_ShouldKeepCaregiverAvailable_WhenAdditional()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        caregiver.VerifyCertificate(
            certificate.Id);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/new-additional.jpg",
            expiryDate: null,
            CreateCurrentDate());

        Assert.Equal(
            CertificateVerificationStatus.Pending,
            certificate.VerificationStatus);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);
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

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        string originalFilePath =
            certificate.FilePath;

        DateOnly? originalExpiryDate =
            certificate.ExpiryDate;

        DateTime certificateUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        DateTime caregiverUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                certificate.Id,
                filePath!,
                expiryDate: null,
                CreateCurrentDate()));

        Assert.Equal(
            originalFilePath,
            certificate.FilePath);

        Assert.Equal(
            originalExpiryDate,
            certificate.ExpiryDate);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Null(certificate.ReviewReason);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);

        Assert.Equal(
            caregiverUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCertificateFile_ShouldRejectExpiredFileWithoutMutation()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        string originalFilePath =
            certificate.FilePath;

        DateOnly? originalExpiryDate =
            certificate.ExpiryDate;

        DateTime certificateUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        DateTime caregiverUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        DateOnly currentDate =
            CreateCurrentDate();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                certificate.Id,
                "certificates/expired.jpg",
                currentDate.AddDays(-1),
                currentDate));

        Assert.Equal(
            originalFilePath,
            certificate.FilePath);

        Assert.Equal(
            originalExpiryDate,
            certificate.ExpiryDate);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);

        Assert.Equal(
            caregiverUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void UpdateCertificateFile_ShouldRejectEmptyCertificateId()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.UpdateCertificateFile(
                CaregiverCertificateId.Empty,
                "certificates/document.jpg",
                expiryDate: null,
                CreateCurrentDate()));
    }

    [Theory]
    [InlineData(CertificateVerificationStatus.Pending)]
    [InlineData(CertificateVerificationStatus.Rejected)]
    [InlineData(CertificateVerificationStatus.Verified)]
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void RemoveCertificate_ShouldRemoveAdditionalCertificateFromAnyStatus(
        CertificateVerificationStatus initialStatus)
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        MakeMedicalCaregiverCompliantAndAvailable(
            caregiver);

        MoveCertificateToStatus(
            caregiver,
            certificate,
            initialStatus);

        caregiver.RemoveCertificate(
            certificate.Id);

        Assert.DoesNotContain(
    caregiver.Certificates,
    existingCertificate =>
        existingCertificate.Id ==
        certificate.Id);

        Assert.DoesNotContain(
            caregiver.Certificates,
            existingCertificate =>
                existingCertificate.Type ==
                CaregiverCertificateType.AdditionalCertificate);

        Assert.Equal(
            2,
            caregiver.Certificates.Count);

        Assert.Contains(
            caregiver.Certificates,
            existingCertificate =>
                existingCertificate.Type ==
                CaregiverCertificateType.PracticeLicense);

        Assert.Contains(
            caregiver.Certificates,
            existingCertificate =>
                existingCertificate.Type ==
                CaregiverCertificateType.GraduationCertificate);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void RemoveCertificate_ShouldAllowAdditionalCapacityToBeReused()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateOnly currentDate =
            CreateCurrentDate();

        for (int certificateNumber = 1;
            certificateNumber <=
            Caregiver.MaximumAdditionalCertificates;
            certificateNumber++)
        {
            caregiver.AddCertificate(
                CaregiverCertificateType.AdditionalCertificate,
                $"certificates/additional-{certificateNumber}.jpg",
                expiryDate: null,
                currentDate);
        }

        CaregiverCertificate certificateToRemove =
            caregiver.Certificates.First();

        caregiver.RemoveCertificate(
            certificateToRemove.Id);

        caregiver.AddCertificate(
            CaregiverCertificateType.AdditionalCertificate,
            "certificates/replacement-additional.jpg",
            expiryDate: null,
            currentDate);

        Assert.Equal(
            Caregiver.MaximumAdditionalCertificates,
            caregiver.Certificates.Count);

        Assert.DoesNotContain(
            caregiver.Certificates,
            certificate =>
                certificate.Id ==
                certificateToRemove.Id);

        Assert.Contains(
            caregiver.Certificates,
            certificate =>
                certificate.FilePath ==
                "certificates/replacement-additional.jpg");
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

        DateTime certificateUpdatedOnUtc =
            certificate.UpdatedOnUtc;

        DateTime caregiverUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificate(
                certificate.Id));

        CaregiverCertificate remainingCertificate =
            Assert.Single(
                caregiver.Certificates);

        Assert.Equal(
            certificate.Id,
            remainingCertificate.Id);

        Assert.Equal(
            certificateUpdatedOnUtc,
            certificate.UpdatedOnUtc);

        Assert.Equal(
            caregiverUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveCertificate_ShouldRejectRejectedMandatoryCertificate()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.RejectCertificate(
            certificate.Id,
            "Invalid License.");

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificate(
                certificate.Id));

        CaregiverCertificate remainingCertificate =
            Assert.Single(
                caregiver.Certificates);

        Assert.Equal(
            CertificateVerificationStatus.Rejected,
            remainingCertificate.VerificationStatus);
    }

    [Fact]
    public void RemoveCertificate_ShouldRejectEmptyCertificateId()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificate(
                CaregiverCertificateId.Empty));

        Assert.Empty(caregiver.Certificates);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveCertificate_ShouldRejectCertificateFromAnotherCaregiver()
    {
        Caregiver firstCaregiver =
            CreateMedicalCaregiver();

        Caregiver secondCaregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                firstCaregiver,
                CaregiverCertificateType.AdditionalCertificate);

        DateTime secondCaregiverUpdatedOnUtc =
            secondCaregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => secondCaregiver.RemoveCertificate(
                certificate.Id));

        Assert.Single(firstCaregiver.Certificates);
        Assert.Empty(secondCaregiver.Certificates);

        Assert.Equal(
            secondCaregiverUpdatedOnUtc,
            secondCaregiver.UpdatedOnUtc);
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
            19);
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
                    "Rejected during review.");
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
                    "Approval was revoked.");
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(status),
                    status,
                    "Unsupported Certificate status.");
        }
    }

    private static void MakeMedicalCaregiverCompliantAndAvailable(
        Caregiver caregiver)
    {
        DateOnly currentDate =
            CreateCurrentDate();

        EnsureMandatoryCertificateIsVerified(
            caregiver,
            CaregiverCertificateType.PracticeLicense);

        EnsureMandatoryCertificateIsVerified(
            caregiver,
            CaregiverCertificateType.GraduationCertificate);

        if (caregiver.Status !=
            CaregiverStatus.Active)
        {
            caregiver.Activate();
        }

        caregiver.BecomeAvailable(
            currentDate);
    }

    private static void EnsureMandatoryCertificateIsVerified(
        Caregiver caregiver,
        CaregiverCertificateType certificateType)
    {
        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(
                    certificate =>
                        certificate.Type ==
                        certificateType);

        certificate ??=
            AddCertificate(
                caregiver,
                certificateType);

        if (certificate.VerificationStatus ==
            CertificateVerificationStatus.Pending)
        {
            caregiver.VerifyCertificate(
                certificate.Id);

            return;
        }

        if (certificate.VerificationStatus !=
            CertificateVerificationStatus.Verified)
        {
            throw new InvalidOperationException(
                $"The test Certificate {certificateType} " +
                $"cannot be made compliant from status " +
                $"{certificate.VerificationStatus}.");
        }
    }
}