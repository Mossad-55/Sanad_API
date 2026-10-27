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

    /// <summary>
    /// With no valid approved current version (a brand-new pending upload or an
    /// initial rejection), a replacement upload becomes the effective pending
    /// version and the row's columns mirror it.
    /// </summary>
    [Theory]
    [InlineData(CertificateVerificationStatus.Pending)]
    [InlineData(CertificateVerificationStatus.Rejected)]
    public void UpdateCertificateFile_ShouldMakeReplacementEffective_WhenNoValidCurrent(
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

    /// <summary>
    /// A replacement upload against an already-approved (or revoked)
    /// qualification is appended as a pending version only: the current
    /// effective scan keeps the row's columns and the caregiver's eligibility
    /// until the replacement is itself reviewed.
    /// </summary>
    [Theory]
    [InlineData(CertificateVerificationStatus.Verified)]
    [InlineData(CertificateVerificationStatus.Revoked)]
    public void UpdateCertificateFile_ShouldKeepCurrentEffective_WhenReplacementPending(
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

        string originalFilePath =
            certificate.FilePath;

        DateOnly? originalExpiryDate =
            certificate.ExpiryDate;

        caregiver.UpdateCertificateFile(
            certificate.Id,
            "  certificates/new-document.jpg  ",
            CreateCurrentDate()
                .AddYears(1),
            CreateCurrentDate(),
            CaregiverTestData.CurrentUtc);

        Assert.Equal(
            initialStatus,
            certificate.VerificationStatus);

        Assert.Equal(
            originalFilePath,
            certificate.FilePath);

        Assert.Equal(
            originalExpiryDate,
            certificate.ExpiryDate);

        Assert.True(certificate.HasPendingReplacement);

        Assert.Equal(
            2,
            certificate.VisibleVersions.Count);
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
    public void RemoveCertificate_ShouldTombstoneAdditionalFromAnyStatus(
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

        // Removal is a logical tombstone, never a physical delete: the row is
        // retained for authorized history but leaves the live profile.
        Assert.True(certificate.IsRemoved);

        Assert.DoesNotContain(
            caregiver.GetActiveCertificates(),
            existing =>
                existing.Id ==
                certificate.Id);

        Assert.Contains(
            caregiver.GetRemovedCertificates(),
            existing =>
                existing.Id ==
                certificate.Id);

        Assert.Contains(
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

        // The tombstoned qualification frees its Additional slot even though
        // its row is retained for audit.
        Assert.Equal(
            Caregiver.MaximumAdditionalCertificates,
            caregiver.GetActiveCertificates().Count);

        Assert.Equal(
            Caregiver.MaximumAdditionalCertificates + 1,
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

    /// <summary>
    /// The aggregate guard behind per-version history removal: a superseded
    /// history version is removed (idempotently), while the current effective
    /// and the live pending versions are refused.
    /// </summary>
    [Fact]
    public void RemoveCertificateVersion_ShouldRemoveHistoryButRefuseCurrentAndPending()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.PracticeLicense);

        caregiver.VerifyCertificate(
            certificate.Id);

        CaregiverCertificateVersionId currentVersionId =
            certificate.CurrentVersion!.Id;

        // A replacement becomes the live pending version; the approved upload
        // stays effective until the replacement is reviewed.
        caregiver.UpdateCertificateFile(
            certificate.Id,
            "certificates/replacement.jpg",
            expiryDate: null,
            CreateCurrentDate(),
            CaregiverTestData.CurrentUtc);

        CaregiverCertificateVersionId pendingVersionId =
            certificate.PendingVersion!.Id;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificateVersion(
                certificate.Id,
                currentVersionId,
                UserId.New(),
                CaregiverTestData.CurrentUtc));

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificateVersion(
                certificate.Id,
                pendingVersionId,
                UserId.New(),
                CaregiverTestData.CurrentUtc));

        // Promoting the replacement turns the approved upload into history.
        caregiver.VerifyCertificate(
            certificate.Id);

        Assert.True(
            caregiver.RemoveCertificateVersion(
                certificate.Id,
                currentVersionId,
                UserId.New(),
                CaregiverTestData.CurrentUtc));

        // Idempotent repeat: no second change, no exception.
        Assert.False(
            caregiver.RemoveCertificateVersion(
                certificate.Id,
                currentVersionId,
                UserId.New(),
                CaregiverTestData.CurrentUtc));

        Assert.True(
            certificate.Versions
                .Single(version => version.Id == currentVersionId)
                .IsRemoved);
    }

    /// <summary>
    /// The rejected candidate that still backs the effective snapshot (no
    /// approved current) is protected: it is the only source of the displayed
    /// status and the legacy snapshot download, so removing it must be refused
    /// rather than leaving those pointing at a removed file. Only a later
    /// upload supersedes it into genuine history.
    /// </summary>
    [Fact]
    public void RemoveCertificateVersion_ShouldProtectRejectedCandidateBackingTheSnapshot()
    {
        Caregiver caregiver =
            CreateMedicalCaregiver();

        CaregiverCertificate certificate =
            AddCertificate(
                caregiver,
                CaregiverCertificateType.AdditionalCertificate);

        caregiver.RejectCertificate(
            certificate.Id,
            "Unreadable scan.");

        CaregiverCertificateVersion rejected =
            certificate.Versions.Single();

        Assert.True(rejected.IsHistory);
        Assert.False(certificate.CanRemoveVersion(rejected));

        Assert.Throws<DomainException>(
            () => caregiver.RemoveCertificateVersion(
                certificate.Id,
                rejected.Id,
                UserId.New(),
                CaregiverTestData.CurrentUtc));
    }

    /// <summary>
    /// When a mandatory qualification still has a valid approved version, a
    /// rejected replacement never knocks the Active caregiver off the roster:
    /// the approved current stays in force and the rejection is retained as
    /// history rather than becoming the effective label.
    /// </summary>
    [Theory]
    [InlineData(CaregiverCertificateType.PracticeLicense)]
    [InlineData(CaregiverCertificateType.GraduationCertificate)]
    public void RejectCertificate_ShouldKeepActive_WhenMandatoryReplacementRejectedWithValidCurrent(
        CaregiverCertificateType certificateType)
    {
        Caregiver caregiver =
            CreateActiveMedicalCaregiver();

        CaregiverCertificate certificate =
            GetCertificate(
                caregiver,
                certificateType);

        string originalFilePath =
            certificate.FilePath;

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
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        // The approved current is the honest effective label; the rejected
        // replacement is retained history.
        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        Assert.Equal(
            originalFilePath,
            certificate.FilePath);

        Assert.False(certificate.HasPendingReplacement);
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