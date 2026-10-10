using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class FanoutTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    [Theory]
    [InlineData("CareHomes", "BookingAccepted", true, "BookingUpdates")]
    [InlineData("ElderlyCheckIn", "NegativeCheckIn", true, "CheckInAlerts")]
    [InlineData("ElderlyHelpRequest", "HelpRequestCreated", true, "HelpRequestAlerts")]
    [InlineData("MedicationReminders", "MedicationLate", true, "MedicationReminders")]
    [InlineData("ElderlySos", "SosCreated", true, "HelpRequestAlerts")]
    [InlineData("CommunityNotifications", "PostPublished", true, "CommunityNotifications")]
    [InlineData("CommunityNotifications", "PostLiked", false, "")]
    [InlineData("MessagesFromFamilies", "NewMessage", true, "MessagesFromFamilies")]
    [InlineData("Subscriptions", "Renewed", false, "")]
    [InlineData("Payouts", "PayoutRecorded", false, "")]
    public void FanoutPolicy_MapsOnlyPushEligibleEvents(string category, string type, bool expected, string expectedPreference)
    {
        Notification notification = Notification.Create(Guid.NewGuid(), category, type, "title", "body",
            "Booking", Guid.NewGuid(), Now);
        bool shouldPush = PushFanoutPolicy.TryGetPreferenceKey(notification, out string preference);
        Assert.Equal(expected, shouldPush);
        Assert.Equal(expectedPreference, preference);
    }

    [Fact]
    public async Task SavingEligibleNotification_AtomicallyEnqueuesIdOnlyPushPayload()
    {
        await using var context = CreateContext();
        Guid userId = Guid.NewGuid();
        Guid entityId = Guid.NewGuid();
        context.DeviceTokens.Add(DeviceToken.Register(userId, DevicePlatform.IOS, "synthetic_fcm_token_0123456789", null, Now));
        Notification notification = Notification.Create(userId, "ElderlyCheckIn", "NegativeCheckIn", "Check-in missed",
            "Care recipient name is not payload data", "CheckIn", entityId, Now, "test:notification:1");
        context.Notifications.Add(notification);

        await context.SaveChangesAsync();

        PushOutboxMessage outbox = Assert.Single(context.PushOutboxMessages);
        Assert.Equal(userId, outbox.RecipientUserId);
        Assert.Equal("CheckInAlerts", outbox.PreferenceKey);
        Assert.Contains(notification.Id.ToString("D"), outbox.ReadData()["deepLink"]);
        Assert.Equal(entityId.ToString("D"), outbox.ReadData()["entityId"]);
        Assert.DoesNotContain("Care recipient name", outbox.DataJson, StringComparison.Ordinal);
        Assert.Equal(PushOutboxStatus.Pending, outbox.Status);
    }

    [Fact]
    public async Task SavingIneligibleNotification_DoesNotEnqueuePush()
    {
        await using var context = CreateContext();
        Guid userId = Guid.NewGuid();
        context.DeviceTokens.Add(DeviceToken.Register(userId, DevicePlatform.Android, "synthetic_fcm_token_0123456789", null, Now));
        context.Notifications.Add(Notification.Create(userId, "CommunityNotifications", "PostLiked", "Like", "",
            "Post", Guid.NewGuid(), Now));
        await context.SaveChangesAsync();
        Assert.Empty(context.PushOutboxMessages);
    }

    private static PushOutboxMessage NewPushMessage() => PushOutboxMessage.Create(Guid.NewGuid(), "title", "body",
        new Dictionary<string, string> { ["entityId"] = Guid.NewGuid().ToString("D") }, "test:push:fanout", "BookingUpdates", Now);

    private static NotificationsDbContext CreateContext() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
