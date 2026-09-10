using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserChangeNotificationPreferencesTests
{
    [Fact]
    public void ChangeNotificationPreferences_ShouldSetValuesAndUpdatedOnUtc()
    {
        User user =
            CreateUser();

        user.ClearDomainEvents();

        DateTime changedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        user.ChangeNotificationPreferences(
            NotificationPreferences.Create(
                checkInAlerts: true,
                medicationReminders: false,
                bookingUpdates: true,
                communityNotifications: false),
            changedOnUtc);

        Assert.True(
            user.NotificationPreferences.CheckInAlerts);

        Assert.False(
            user.NotificationPreferences.MedicationReminders);

        Assert.True(
            user.NotificationPreferences.BookingUpdates);

        Assert.False(
            user.NotificationPreferences.CommunityNotifications);

        Assert.Equal(
            changedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangeNotificationPreferences_ShouldNoOpWhenUnchanged()
    {
        User user =
            CreateUser();

        DateTime initialUpdatedOnUtc =
            user.UpdatedOnUtc;

        user.ClearDomainEvents();

        user.ChangeNotificationPreferences(
            NotificationPreferences.Create(
                checkInAlerts: true,
                medicationReminders: true,
                bookingUpdates: true,
                communityNotifications: true),
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.True(
            user.NotificationPreferences.CheckInAlerts);

        Assert.True(
            user.NotificationPreferences.MedicationReminders);

        Assert.True(
            user.NotificationPreferences.BookingUpdates);

        Assert.True(
            user.NotificationPreferences.CommunityNotifications);

        Assert.Equal(
            initialUpdatedOnUtc,
            user.UpdatedOnUtc);
    }

    [Fact]
    public void ChangeNotificationPreferences_ShouldRejectBlockedUser()
    {
        User user =
            CreateUser();

        user.Block(
            "Security block.",
            CreateUtcDateTime());

        user.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => user.ChangeNotificationPreferences(
                NotificationPreferences.Create(
                    checkInAlerts: false,
                    medicationReminders: false,
                    bookingUpdates: false,
                    communityNotifications: false),
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.True(
            user.NotificationPreferences.CheckInAlerts);

        Assert.True(
            user.NotificationPreferences.MedicationReminders);

        Assert.True(
            user.NotificationPreferences.BookingUpdates);

        Assert.True(
            user.NotificationPreferences.CommunityNotifications);
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create(
                "mohamed@example.com"),
            PhoneNumber.Create(
                "+201001234567"));
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
