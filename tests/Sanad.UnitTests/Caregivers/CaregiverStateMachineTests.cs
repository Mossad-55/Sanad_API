using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverStateMachineTests
{
    [Fact]
    public void SubmitForReview_ShouldMoveReadyOnboardingToPendingReview()
    {
        Caregiver caregiver =
            CreateReadyCaregiver();

        DateTime submittedOnUtc =
            CreateUtcDateTime();

        caregiver.SubmitForReview(
            submittedOnUtc,
            CaregiverTestData.CurrentDate);

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            submittedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SubmitForReview_ShouldRejectIncompleteCaregiver()
    {
        Caregiver caregiver =
            CreateRawCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SubmitForReview(
                CreateUtcDateTime(),
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RequestCorrection_ShouldMovePendingReviewToNeedsCorrection()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        DateTime reviewedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        caregiver.RequestCorrection(
            "  Update the Practice License image.  ",
            reviewedOnUtc);

        Assert.Equal(
            CaregiverStatus.NeedsCorrection,
            caregiver.Status);

        Assert.Equal(
            "Update the Practice License image.",
            caregiver.StatusReason);

        Assert.Equal(
            reviewedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestCorrection_ShouldRequireReason(
        string? reason)
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RequestCorrection(
                reason!,
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void ResubmitForReview_ShouldMoveReadyCorrectionsToPendingReview()
    {
        Caregiver caregiver =
            CreateNeedsCorrectionCaregiver();

        DateTime resubmittedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        caregiver.ResubmitForReview(
            resubmittedOnUtc,
            CaregiverTestData.CurrentDate);

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            resubmittedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void ResubmitForReview_ShouldRejectIncompleteCorrections()
    {
        Caregiver caregiver =
            CreateNeedsCorrectionCaregiver();

        var service =
            Assert.Single(
                caregiver.ServiceSelections);

        caregiver.RemoveService(
            service.Id);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        string originalReason =
            caregiver.StatusReason!;

        Assert.Throws<DomainException>(
            () => caregiver.ResubmitForReview(
                CreateUtcDateTime()
                    .AddMinutes(2),
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.NeedsCorrection,
            caregiver.Status);

        Assert.Equal(
            originalReason,
            caregiver.StatusReason);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void Approve_ShouldMoveReadyPendingReviewToActive()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        DateTime approvedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        caregiver.Approve(
            approvedOnUtc,
            CaregiverTestData.CurrentDate);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            approvedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RejectApplication_ShouldCreateFinalRejectedState()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        caregiver.RejectApplication(
            "  Required credentials are invalid.  ",
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Equal(
            CaregiverStatus.Rejected,
            caregiver.Status);

        Assert.Equal(
            "Required credentials are invalid.",
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void RejectedCaregiver_ShouldNotDirectlyResubmit()
    {
        Caregiver caregiver =
            CreateRejectedCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.ResubmitForReview(
                CreateUtcDateTime()
                    .AddMinutes(2),
                CaregiverTestData.CurrentDate));

        Assert.Throws<DomainException>(
            () => caregiver.SubmitForReview(
                CreateUtcDateTime()
                    .AddMinutes(2),
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.Rejected,
            caregiver.Status);
    }

    [Fact]
    public void Suspend_ShouldMoveActiveToSuspended()
    {
        Caregiver caregiver =
            CreateActiveCaregiver();

        caregiver.Suspend(
            "  Compliance review required.  ",
            CreateUtcDateTime()
                .AddMinutes(2));

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);

        Assert.Equal(
            "Compliance review required.",
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void Reactivate_ShouldMoveReadySuspendedToActive()
    {
        Caregiver caregiver =
            CreateSuspendedCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        DateTime reactivatedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(3);

        caregiver.Reactivate(
            reactivatedOnUtc,
            CaregiverTestData.CurrentDate);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            reactivatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void SubmitForReview_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        Caregiver caregiver =
            CreateReadyCaregiver();

        DateTime invalidTime =
            DateTime.SpecifyKind(
                new DateTime(
                    2026,
                    8,
                    20,
                    10,
                    0,
                    0),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => caregiver.SubmitForReview(
                invalidTime,
                CaregiverTestData.CurrentDate));

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);
    }

    private static Caregiver CreateRawCaregiver()
    {
        return Caregiver.Create(
            UserId.New(),
            CaregiverType.Companion);
    }

    private static Caregiver CreateReadyCaregiver()
    {
        Caregiver caregiver =
            CreateRawCaregiver();

        CaregiverTestData
            .EnsureReadyForSubmission(
                caregiver);

        return caregiver;
    }

    private static Caregiver CreatePendingReviewCaregiver()
    {
        Caregiver caregiver =
            CreateReadyCaregiver();

        caregiver.SubmitForReview(
            CreateUtcDateTime(),
            CaregiverTestData.CurrentDate);

        return caregiver;
    }

    private static Caregiver CreateNeedsCorrectionCaregiver()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        caregiver.RequestCorrection(
            "Correction required.",
            CreateUtcDateTime()
                .AddMinutes(1));

        return caregiver;
    }

    private static Caregiver CreateActiveCaregiver()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        CaregiverTestData
            .EnsureReadyForActivation(
                caregiver);

        caregiver.Approve(
            CreateUtcDateTime()
                .AddMinutes(1),
            CaregiverTestData.CurrentDate);

        return caregiver;
    }

    private static Caregiver CreateSuspendedCaregiver()
    {
        Caregiver caregiver =
            CreateActiveCaregiver();

        caregiver.Suspend(
            "Suspended for review.",
            CreateUtcDateTime()
                .AddMinutes(2));

        return caregiver;
    }

    private static Caregiver CreateRejectedCaregiver()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        caregiver.RejectApplication(
            "Final rejection.",
            CreateUtcDateTime()
                .AddMinutes(1));

        return caregiver;
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