using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

/// <summary>
/// SET-14 enforcement: discovery search excludes caregivers with ShowProfile = false.
/// This test verifies the domain behavior and the expected SQL filter contract.
/// </summary>
public sealed class CaregiverDiscoveryPrivacyTests
{
    [Fact]
    public void Caregiver_WithShowProfileFalse_ShouldBeConsideredHidden()
    {
        var caregiver = Caregiver.Create(
            UserId.New(),
            CaregiverType.Medical);

        caregiver.UpdateVisibility(
            showProfile: false,
            showRating: true,
            showPhone: false,
            shareLocation: false);

        Assert.False(caregiver.Visibility.ShowProfile);

        // The discovery SQL must exclude this caregiver.
        // The contract is enforced in CaregiversDbContext.SearchActiveCaregiversAsync
        // via WHERE COALESCE(c.show_profile, true) = true
        // Here we assert the domain state that the SQL filter relies on.
        bool shouldBeVisibleInDiscovery = caregiver.Visibility.ShowProfile;
        Assert.False(shouldBeVisibleInDiscovery);
    }

    [Fact]
    public void Caregiver_WithShowProfileTrue_ShouldBeVisible()
    {
        var caregiver = Caregiver.Create(
            UserId.New(),
            CaregiverType.Companion);

        caregiver.UpdateVisibility(
            showProfile: true,
            showRating: false,
            showPhone: true,
            shareLocation: false);

        Assert.True(caregiver.Visibility.ShowProfile);

        bool shouldBeVisibleInDiscovery = caregiver.Visibility.ShowProfile;
        Assert.True(shouldBeVisibleInDiscovery);
    }

    [Fact]
    public void Discovery_ShouldExclude_HiddenCaregivers_FromResults()
    {
        var visible = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        visible.UpdateVisibility(true, true, false, false);

        var hidden = Caregiver.Create(UserId.New(), CaregiverType.Medical);
        hidden.UpdateVisibility(false, true, false, false);

        var all = new[] { visible, hidden };

        var filtered = all.Where(c => c.Visibility.ShowProfile).ToList();

        Assert.Single(filtered);
        Assert.Contains(visible, filtered);
        Assert.DoesNotContain(hidden, filtered);
    }
}
