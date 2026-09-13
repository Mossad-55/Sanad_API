using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class NotificationPreferencesTests
{
    [Fact]
    public void CreateDefault_ShouldEnableAllEightToggles()
    {
        NotificationPreferences preferences =
            NotificationPreferences.CreateDefault();

        Assert.True(preferences.CheckInAlerts);
        Assert.True(preferences.MedicationReminders);
        Assert.True(preferences.BookingUpdates);
        Assert.True(preferences.CommunityNotifications);
        Assert.True(preferences.FamilyActivityAlerts);
        Assert.True(preferences.NewOrders);
        Assert.True(preferences.MessagesFromFamilies);
        Assert.True(preferences.SystemNotifications);
    }

    [Fact]
    public void Create_ShouldMapAllEightToggles()
    {
        NotificationPreferences preferences =
            NotificationPreferences.Create(
                checkInAlerts: true,
                medicationReminders: false,
                bookingUpdates: true,
                communityNotifications: false,
                familyActivityAlerts: true,
                newOrders: false,
                messagesFromFamilies: true,
                systemNotifications: false);

        Assert.True(preferences.CheckInAlerts);
        Assert.False(preferences.MedicationReminders);
        Assert.True(preferences.BookingUpdates);
        Assert.False(preferences.CommunityNotifications);
        Assert.True(preferences.FamilyActivityAlerts);
        Assert.False(preferences.NewOrders);
        Assert.True(preferences.MessagesFromFamilies);
        Assert.False(preferences.SystemNotifications);
    }

    [Fact]
    public void Equality_ShouldConsiderAllEightToggles()
    {
        NotificationPreferences left =
            NotificationPreferences.CreateDefault();

        NotificationPreferences right =
            NotificationPreferences.CreateDefault();

        Assert.Equal(left, right);

        NotificationPreferences differsOnNewField =
            NotificationPreferences.Create(
                checkInAlerts: true,
                medicationReminders: true,
                bookingUpdates: true,
                communityNotifications: true,
                familyActivityAlerts: true,
                newOrders: true,
                messagesFromFamilies: true,
                systemNotifications: false);

        Assert.NotEqual(left, differsOnNewField);
    }
}
