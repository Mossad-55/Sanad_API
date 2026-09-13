using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverDeactivationTests
{
    private const string DeactivationReason =
        "Self-deleted by the account owner.";

    [Fact]
    public void Deactivate_SetsStatusReasonAndUnavailable()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Companion);

        caregiver.TransitionToActive();

        caregiver.BecomeAvailable(
            CaregiverTestData.CurrentDate);

        Assert.Equal(
            CaregiverAvailability.Available,
            caregiver.Availability);

        DateTime deactivationTime =
            CaregiverTestData.CurrentUtc
                .AddMinutes(5);

        caregiver.Deactivate(
            DeactivationReason,
            deactivationTime);

        Assert.Equal(
            CaregiverStatus.Deactivated,
            caregiver.Status);

        Assert.Equal(
            DeactivationReason,
            caregiver.StatusReason);

        Assert.Equal(
            CaregiverAvailability.Unavailable,
            caregiver.Availability);

        Assert.Equal(
            deactivationTime,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_IsIdempotent_SecondCallKeepsState()
    {
        Caregiver caregiver =
            Caregiver.Create(
                UserId.New(),
                CaregiverType.Medical);

        caregiver.TransitionToActive();

        DateTime firstDeactivation =
            CaregiverTestData.CurrentUtc
                .AddMinutes(5);

        caregiver.Deactivate(
            DeactivationReason,
            firstDeactivation);

        CaregiverStatus statusAfterFirst =
            caregiver.Status;

        string? reasonAfterFirst =
            caregiver.StatusReason;

        CaregiverAvailability availabilityAfterFirst =
            caregiver.Availability;

        DateTime updatedOnUtcAfterFirst =
            caregiver.UpdatedOnUtc;

        // A retry after a partial failure must converge without rewriting the
        // original deactivation record.
        caregiver.Deactivate(
            "Second, later attempt.",
            firstDeactivation.AddMinutes(30));

        Assert.Equal(
            statusAfterFirst,
            caregiver.Status);

        Assert.Equal(
            CaregiverStatus.Deactivated,
            caregiver.Status);

        Assert.Equal(
            reasonAfterFirst,
            caregiver.StatusReason);

        Assert.Equal(
            availabilityAfterFirst,
            caregiver.Availability);

        Assert.Equal(
            updatedOnUtcAfterFirst,
            caregiver.UpdatedOnUtc);
    }
}
