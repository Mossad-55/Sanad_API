using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Domain.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.ChatPlus;

/// <summary>
/// Phase 5 push tests: canonical Chat/NewMessage routing, legacy mapping,
/// preference backward compatibility (explicit/mirror/preserve), defaults,
/// ids-only payloads, and the actual producer-to-outbox path with a
/// registered device. Live provider delivery stays a Phase 7 release gate.
/// </summary>
public sealed class ChatPlusPushTests
{
    private static DateTime Now =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    private static IdentityDbContext CreateIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NotificationsDbContext CreateNotificationsDb() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void FanOut_ChatNewMessageRoutesToChatMessages()
    {
        Notification chat = Notification.Create(
            Guid.CreateVersion7(), "Chat", "NewMessage",
            "New chat message", "Conversation: c. Message: m.",
            "ChatConversation", Guid.CreateVersion7(), Now,
            "chat-message:k:r");

        Assert.True(PushFanoutPolicy.TryGetPreferenceKey(chat, out string key));
        Assert.Equal("ChatMessages", key);

        // Legacy rows keep working through the same key.
        Notification legacy = Notification.Create(
            Guid.CreateVersion7(), "MessagesFromFamilies", "ChatMessageReceived",
            "t", "b", "ChatConversation", Guid.CreateVersion7(), Now, "legacy:k");

        Assert.True(PushFanoutPolicy.TryGetPreferenceKey(legacy, out string legacyKey));
        Assert.Equal("ChatMessages", legacyKey);

        IReadOnlyDictionary<string, string> data = PushFanoutPolicy.BuildData(chat);
        Assert.DoesNotContain("text", string.Join(",", data.Keys), StringComparison.OrdinalIgnoreCase);
        Assert.True(data.ContainsKey("entityId"));
        Assert.StartsWith("sanad://", data["deepLink"], StringComparison.Ordinal);
    }

    [Fact]
    public void FanOut_ChatPayloadCarriesConversationAndMessageIds()
    {
        Guid conversationId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();
        Guid recipientId = Guid.CreateVersion7();

        Notification chat = Notification.Create(
            recipientId, "Chat", "NewMessage",
            "New chat message",
            $"Conversation: {conversationId}. Message: {messageId}.",
            "ChatConversation", conversationId, Now,
            CreateChatMessageNotificationCommandHandler.BuildIdempotencyKey(messageId, recipientId));

        IReadOnlyDictionary<string, string> data = PushFanoutPolicy.BuildData(chat);

        Assert.Equal(conversationId.ToString("D"), data["conversationId"]);
        Assert.Equal(messageId.ToString("D"), data["messageId"]);
        Assert.Equal(conversationId.ToString("D"), data["entityId"]);
        Assert.DoesNotContain(messageId.ToString("D"), data["deepLink"], StringComparison.Ordinal);

        // Non-chat rows never gain chat keys.
        Notification booking = Notification.Create(
            recipientId, "CareHomes", "BookingConfirmed",
            "t", "b", "CareHomeBooking", Guid.CreateVersion7(), Now, null);

        IReadOnlyDictionary<string, string> bookingData = PushFanoutPolicy.BuildData(booking);
        Assert.DoesNotContain("conversationId", string.Join(",", bookingData.Keys), StringComparison.Ordinal);
        Assert.DoesNotContain("messageId", string.Join(",", bookingData.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void Preferences_DefaultsEnableChatAndMirrorLegacyOptOut()
    {
        NotificationPreferences @default = NotificationPreferences.CreateDefault();
        Assert.True(@default.ChatMessages);

        NotificationPreferences legacyOptOut = NotificationPreferences.Create(
            true, true, true, true, true, true, false, true);
        Assert.False(legacyOptOut.ChatMessages);

        NotificationPreferences explicitOptIn = NotificationPreferences.Create(
            true, true, true, true, true, true, false, true, true, chatMessages: true);
        Assert.True(explicitOptIn.ChatMessages);
    }

    [Fact]
    public async Task Compat_ExplicitChatMessagesWinsOverLegacy()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateMyNotificationPreferencesCommandHandler(db, new FixedClock());

        var result = await handler.Handle(
            new UpdateMyNotificationPreferencesCommand(
                user.Id, true, true, true, true, true, true, false, true, null,
                ChatMessages: true),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ChatMessages);
        Assert.False(result.Value.MessagesFromFamilies);
    }

    [Fact]
    public async Task Compat_LegacyChangeMirrorsToChatMessages()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateMyNotificationPreferencesCommandHandler(db, new FixedClock());

        // Flip only the legacy flag: chatMessages follows it.
        var result = await handler.Handle(
            new UpdateMyNotificationPreferencesCommand(
                user.Id, true, true, true, true, true, true, false, true, null,
                ChatMessages: null),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.MessagesFromFamilies);
        Assert.False(result.Value.ChatMessages);
    }

    [Fact]
    public async Task Compat_OmissionNeverResetsAnOptOut()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateMyNotificationPreferencesCommandHandler(db, new FixedClock());

        // First opt out explicitly.
        var optOut = await handler.Handle(
            new UpdateMyNotificationPreferencesCommand(
                user.Id, true, true, true, true, true, true, true, true, null,
                ChatMessages: false),
            default);
        Assert.True(optOut.IsSuccess);
        Assert.False(optOut.Value.ChatMessages);

        // Then a full-replacement update that omits chatMessages and does
        // not change the legacy flag: the opt-out survives.
        var preserved = await handler.Handle(
            new UpdateMyNotificationPreferencesCommand(
                user.Id, true, true, true, true, true, true, true, true, null,
                ChatMessages: null),
            default);

        Assert.True(preserved.IsSuccess);
        Assert.False(preserved.Value.ChatMessages);
    }

    [Fact]
    public async Task Producer_ChatNewMessageEnqueuesEligiblePushWithIdsOnly()
    {
        await using IdentityDbContext identity = CreateIdentityDb();
        await using NotificationsDbContext notifications = CreateNotificationsDb();

        User recipient = CreateUser();
        identity.Users.Add(recipient);
        await identity.SaveChangesAsync();

        notifications.DeviceTokens.Add(DeviceToken.Register(
            recipient.Id.Value, DevicePlatform.Android, "fcm-test-token", "1.0", Now));
        await notifications.SaveChangesAsync();

        Guid conversationId = Guid.CreateVersion7();
        Guid messageId = Guid.CreateVersion7();

        var producer = new CreateChatMessageNotificationCommandHandler(
            notifications,
            Sanad.UnitTests.Notifications.FixedNotificationLanguageGateway.ArabicDefault);
        Sanad.BuildingBlocks.Application.Results.Result<Guid> created = await producer.Handle(
            new CreateChatMessageNotificationCommand(
                recipient.Id.Value, conversationId, messageId, Now),
            default);

        Assert.True(created.IsSuccess);
        Guid notificationId = created.Value;
        Assert.NotEqual(Guid.Empty, notificationId);

        PushOutboxMessage outbox = await notifications.PushOutboxMessages.SingleAsync();
        Assert.Equal("ChatMessages", outbox.PreferenceKey);

        IReadOnlyDictionary<string, string> data = outbox.ReadData();
        Assert.Equal(conversationId.ToString("D"), data["entityId"]);
        string joined = string.Join(";", data.Values);
        Assert.DoesNotContain(messageId.ToString("N"), joined, StringComparison.Ordinal);
        Assert.Contains(conversationId.ToString("D"), joined, StringComparison.Ordinal);

        Notification row = await notifications.Notifications.SingleAsync();
        Assert.Equal("Chat", row.Category);
        Assert.Equal("NewMessage", row.Type);
        Assert.DoesNotContain("secret", row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
    }

    private static User CreateUser() =>
        User.Create(
            FullName.Create("Test"),
            FullName.Create("User"),
            null,
            PhoneNumber.Create("+201001234567"));

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow => Now;
    }
}
