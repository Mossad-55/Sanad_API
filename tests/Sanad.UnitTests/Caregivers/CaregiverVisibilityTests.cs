using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverVisibilityTests
{
    [Fact]
    public void Caregiver_ShouldHaveDefaultVisibility_OnCreation()
    {
        var caregiver = Caregiver.Create(
            UserId.New(),
            CaregiverType.Medical);

        Assert.NotNull(caregiver.Visibility);
        Assert.True(caregiver.Visibility.ShowProfile);
        Assert.True(caregiver.Visibility.ShowRating);
        Assert.False(caregiver.Visibility.ShowPhone);
        Assert.False(caregiver.Visibility.ShareLocation);
    }

    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void UpdateVisibility_ShouldStoreNewPreferences(
        CaregiverType type)
    {
        var caregiver = Caregiver.Create(
            UserId.New(),
            type);

        DateTime before = caregiver.UpdatedOnUtc;

        caregiver.UpdateVisibility(
            showProfile: false,
            showRating: false,
            showPhone: true,
            shareLocation: true);

        Assert.False(caregiver.Visibility.ShowProfile);
        Assert.False(caregiver.Visibility.ShowRating);
        Assert.True(caregiver.Visibility.ShowPhone);
        Assert.True(caregiver.Visibility.ShareLocation);
        Assert.True(caregiver.UpdatedOnUtc >= before);
    }

    [Fact]
    public void UpdateVisibility_ShouldBeAllowed_InAnyStatus()
    {
        var caregiver = Caregiver.Create(
            UserId.New(),
            CaregiverType.Companion);

        caregiver.TransitionToActive();

        // Active caregiver should still allow visibility updates
        caregiver.UpdateVisibility(false, true, false, true);

        Assert.False(caregiver.Visibility.ShowProfile);
        Assert.True(caregiver.Visibility.ShowRating);
        Assert.False(caregiver.Visibility.ShowPhone);
        Assert.True(caregiver.Visibility.ShareLocation);
        Assert.Equal(CaregiverStatus.Active, caregiver.Status);
    }

    [Fact]
    public void UpdateVisibility_ShouldReplacePreviousValue()
    {
        var caregiver = Caregiver.Create(
            UserId.New(),
            CaregiverType.Medical);

        caregiver.UpdateVisibility(false, false, true, true);
        var first = caregiver.Visibility;

        caregiver.UpdateVisibility(true, true, false, false);
        var second = caregiver.Visibility;

        Assert.NotEqual(first, second);
        Assert.True(second.ShowProfile);
        Assert.True(second.ShowRating);
        Assert.False(second.ShowPhone);
        Assert.False(second.ShareLocation);
    }
}
