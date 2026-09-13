using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.UnitTests.Caregivers;

public sealed class VisibilityPreferencesTests
{
    [Fact]
    public void Default_ShouldHaveExpectedDefaults()
    {
        var visibility = VisibilityPreferences.Default;

        Assert.True(visibility.ShowProfile);
        Assert.True(visibility.ShowRating);
        Assert.False(visibility.ShowPhone);
        Assert.False(visibility.ShareLocation);
    }

    [Fact]
    public void Create_ShouldUseProvidedValues()
    {
        var visibility = VisibilityPreferences.Create(
            showProfile: false,
            showRating: false,
            showPhone: true,
            shareLocation: true);

        Assert.False(visibility.ShowProfile);
        Assert.False(visibility.ShowRating);
        Assert.True(visibility.ShowPhone);
        Assert.True(visibility.ShareLocation);
    }

    [Fact]
    public void Create_ShouldUseDefaults_WhenNoArguments()
    {
        var visibility = VisibilityPreferences.Create();

        Assert.True(visibility.ShowProfile);
        Assert.True(visibility.ShowRating);
        Assert.False(visibility.ShowPhone);
        Assert.False(visibility.ShareLocation);
    }

    [Fact]
    public void ValueEquality_ShouldWork()
    {
        var first = VisibilityPreferences.Create(true, true, false, false);
        var second = VisibilityPreferences.Create(true, true, false, false);
        var different = VisibilityPreferences.Create(false, true, false, false);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);

        Assert.NotEqual(first, different);
        Assert.True(first != different);
    }
}
