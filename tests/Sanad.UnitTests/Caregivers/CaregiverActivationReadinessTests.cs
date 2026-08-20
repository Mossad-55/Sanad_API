using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverActivationReadinessTests
{
    [Fact]
    public void ValidateActivationReadiness_ShouldAllowReadyCompanion()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Companion);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.ValidateActivationReadiness(
            CaregiverTestData.CurrentDate);
    }

    [Fact]
    public void ValidateActivationReadiness_ShouldRejectPendingMedicalCertificates()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        CaregiverTestData
            .EnsureReadyForSubmission(
                caregiver);

        Assert.Throws<DomainException>(
            () => caregiver
                .ValidateActivationReadiness(
                    CaregiverTestData.CurrentDate));
    }

    [Fact]
    public void ValidateActivationReadiness_ShouldAllowVerifiedMedicalCertificates()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.ValidateActivationReadiness(
            CaregiverTestData.CurrentDate);
    }

    [Fact]
    public void Approve_ShouldRejectIncompletePendingReviewWithoutMutation()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Companion);

        CaregiverTestData
            .EnsureReadyForSubmission(
                caregiver);

        caregiver.SubmitForReview(
            CreateUtcDateTime(),
            CaregiverTestData.CurrentDate);

        var service =
            Assert.Single(
                caregiver.ServiceSelections);

        caregiver.RemoveService(
            service.Id);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.Approve(
                CreateUtcDateTime()
                    .AddMinutes(1),
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void Approve_ShouldRejectPendingMedicalCertificates()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        CaregiverTestData
            .EnsureReadyForSubmission(
                caregiver);

        caregiver.SubmitForReview(
            CreateUtcDateTime(),
            CaregiverTestData.CurrentDate);

        Assert.Throws<DomainException>(
            () => caregiver.Approve(
                CreateUtcDateTime()
                    .AddMinutes(1),
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);
    }

    [Fact]
    public void Approve_ShouldRejectCertificateExpiredAfterSubmission()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        DateOnly currentDate =
            CaregiverTestData.CurrentDate;

        CaregiverTestData
            .EnsureReadyForSubmission(
                caregiver);

        CaregiverCertificate practiceLicense =
            caregiver.Certificates.Single(
                certificate =>
                    certificate.Type ==
                    CaregiverCertificateType.PracticeLicense);

        caregiver.UpdateCertificateFile(
            practiceLicense.Id,
            "certificates/practice-license.jpg",
            expiryDate: currentDate,
            currentDate);

        caregiver.SubmitForReview(
            CreateUtcDateTime(),
            currentDate);

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        Assert.Throws<DomainException>(
            () => caregiver.Approve(
                CreateUtcDateTime()
                    .AddMinutes(1),
                currentDate.AddDays(1)));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);
    }

    [Fact]
    public void Reactivate_ShouldRejectSuspendedCaregiverWithEmptySchedule()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Companion);

        caregiver.TransitionToActive();

        caregiver.Suspend(
            "Schedule review required.",
            CreateUtcDateTime()
                .AddMinutes(2));

        CompanionWeeklySchedule schedule =
            Assert.IsType<CompanionWeeklySchedule>(
                caregiver.CompanionSchedule);

        CompanionAvailabilityWindow window =
            Assert.Single(schedule.Windows);

        caregiver.RemoveCompanionAvailabilityWindow(
            window.BookingType,
            window.DayOfWeek,
            window.StartTime,
            window.EndTime);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        string originalReason =
            caregiver.StatusReason!;

        Assert.Throws<DomainException>(
            () => caregiver.Reactivate(
                CreateUtcDateTime()
                    .AddMinutes(3),
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);

        Assert.Equal(
            originalReason,
            caregiver.StatusReason);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }
}