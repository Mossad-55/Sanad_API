using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverStatusTests
{
    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void Create_ShouldStartInOnboardingStatus(
        CaregiverType caregiverType)
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                caregiverType);

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);
    }

    [Fact]
    public void CaregiverStatus_ShouldContainFinalConfirmedStates()
    {
        CaregiverStatus[] statuses =
            Enum.GetValues<CaregiverStatus>();

        Assert.Equal(
            6,
            statuses.Length);

        Assert.Contains(
            CaregiverStatus.Onboarding,
            statuses);

        Assert.Contains(
            CaregiverStatus.PendingReview,
            statuses);

        Assert.Contains(
            CaregiverStatus.NeedsCorrection,
            statuses);

        Assert.Contains(
            CaregiverStatus.Active,
            statuses);

        Assert.Contains(
            CaregiverStatus.Suspended,
            statuses);

        Assert.Contains(
            CaregiverStatus.Rejected,
            statuses);
    }
}