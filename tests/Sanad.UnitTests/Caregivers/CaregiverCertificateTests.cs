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

        Assert.Null(certificate.RejectionReason);

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
}