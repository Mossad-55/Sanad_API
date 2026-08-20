using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverStateMachineTests
{
    [Fact]
    public void SubmitForReview_ShouldMoveOnboardingToPendingReview()
    {
        Caregiver caregiver =
            CreateCaregiver();

        DateTime submittedOnUtc =
            CreateUtcDateTime();

        caregiver.SubmitForReview(
            submittedOnUtc);

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
    public void SubmitForReview_ShouldRejectNonOnboardingStatus()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SubmitForReview(
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
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

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

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void ResubmitForReview_ShouldMoveNeedsCorrectionToPendingReview()
    {
        Caregiver caregiver =
            CreateNeedsCorrectionCaregiver();

        DateTime resubmittedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        caregiver.ResubmitForReview(
            resubmittedOnUtc);

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            resubmittedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void ResubmitForReview_ShouldRejectWrongStatus()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.ResubmitForReview(
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);
    }

    [Fact]
    public void Approve_ShouldMovePendingReviewToActiveAndRemainUnavailable()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        DateTime approvedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        caregiver.Approve(
            approvedOnUtc);

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
    public void Approve_ShouldRejectWrongStatus()
    {
        Caregiver caregiver =
            CreateCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.Approve(
                CreateUtcDateTime()));

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);
    }

    [Fact]
    public void RejectApplication_ShouldCreateFinalRejectedState()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        DateTime rejectedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        caregiver.RejectApplication(
            "  Required credentials are invalid.  ",
            rejectedOnUtc);

        Assert.Equal(
            CaregiverStatus.Rejected,
            caregiver.Status);

        Assert.Equal(
            "Required credentials are invalid.",
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            rejectedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectApplication_ShouldRequireReason(
        string? reason)
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RejectApplication(
                reason!,
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RejectedCaregiver_ShouldNotDirectlyResubmit()
    {
        Caregiver caregiver =
            CreateRejectedCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.ResubmitForReview(
                CreateUtcDateTime()
                    .AddMinutes(2)));

        Assert.Throws<DomainException>(
            () => caregiver.SubmitForReview(
                CreateUtcDateTime()
                    .AddMinutes(2)));

        Assert.Equal(
            CaregiverStatus.Rejected,
            caregiver.Status);
    }

    [Fact]
    public void Suspend_ShouldMoveActiveToSuspendedAndUnavailable()
    {
        Caregiver caregiver =
            CreateActiveCaregiver();

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        DateTime suspendedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        caregiver.Suspend(
            "  Compliance review required.  ",
            suspendedOnUtc);

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);

        Assert.Equal(
            "Compliance review required.",
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            suspendedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Suspend_ShouldRequireReason(
        string? reason)
    {
        Caregiver caregiver =
            CreateActiveCaregiver();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.Suspend(
                reason!,
                CreateUtcDateTime()
                    .AddMinutes(2)));

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Null(caregiver.StatusReason);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void Suspend_ShouldRejectNonActiveStatus()
    {
        Caregiver caregiver =
            CreatePendingReviewCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.Suspend(
                "Invalid transition.",
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            CaregiverStatus.PendingReview,
            caregiver.Status);
    }

    [Fact]
    public void Reactivate_ShouldMoveSuspendedToActiveAndRemainUnavailable()
    {
        Caregiver caregiver =
            CreateSuspendedCaregiver();

        DateTime reactivatedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(3);

        caregiver.Reactivate(
            reactivatedOnUtc);

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

    [Fact]
    public void Reactivate_ShouldRejectNonSuspendedStatus()
    {
        Caregiver caregiver =
            CreateActiveCaregiver();

        Assert.Throws<DomainException>(
            () => caregiver.Reactivate(
                CreateUtcDateTime()
                    .AddMinutes(2)));

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void SubmitForReview_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        Caregiver caregiver =
            CreateCaregiver();

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
                invalidTime));

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);
    }

    private static Caregiver CreateCaregiver()
    {
        return Caregiver.Create(
            UserId.New(),
            CaregiverType.Companion);
    }

    private static Caregiver CreatePendingReviewCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver();

        caregiver.SubmitForReview(
            CreateUtcDateTime());

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

        caregiver.Approve(
            CreateUtcDateTime()
                .AddMinutes(1));

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