using Ahlena.Modules.Notifications.Domain.Notifications;

namespace Ahlena.Modules.Notifications.Application.Notifications;

public static class PushFanoutPolicy
{
    public static bool TryGetPreferenceKey(Notification notification, out string preferenceKey)
    {
        preferenceKey = notification.Category switch
        {
            "CareHomes" when notification.Type.StartsWith("Booking", StringComparison.Ordinal) => "BookingUpdates",
            "CareHomes" => "SystemNotifications",
            "BookingUpdates" or "CaregiverBookings" => "BookingUpdates",
            "ElderlyCheckIn" => "CheckInAlerts",
            "MedicationReminders" => "MedicationReminders",
            "ElderlyHelpRequest" => "HelpRequestAlerts",
            "ElderlySos" => "HelpRequestAlerts",
            "CommunityNotifications" when notification.Type is "PostPublished" or "PostRejected" => "CommunityNotifications",
            // Chat Plus canonical mapping: the Chat producer's NewMessage
            // consults ChatMessages for all sender types. Legacy
            // MessagesFromFamilies rows map to the same key so old
            // consumers keep working through the compatibility rule.
            "Chat" when notification.Type is "NewMessage" => "ChatMessages",
            "MessagesFromFamilies" when notification.Type is "NewMessage" or "ChatMessageReceived" => "ChatMessages",
            _ => string.Empty
        };
        return preferenceKey.Length > 0;
    }

    public static IReadOnlyDictionary<string, string> BuildData(Notification notification)
    {
        string deepLink = $"ahlena://inbox?notificationId={Uri.EscapeDataString(notification.Id.ToString("D"))}";
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["notificationId"] = notification.Id.ToString("D"),
            ["entityId"] = notification.DestinationEntityId.ToString("D"),
            ["entityKind"] = notification.DestinationEntityKind,
            ["deepLink"] = deepLink
        };

        // Chat Plus provider contract: chat payloads carry the string
        // conversation/message ids alongside the inbox identity above — ids
        // only, never content. The destination of a chat row IS its
        // conversation; the message id rides the stable producer
        // idempotency key (chat-message:{messageId:N}:{recipientId:N}).
        if (notification.Category is "Chat" or "MessagesFromFamilies" &&
            notification.Type is "NewMessage" or "ChatMessageReceived")
        {
            data["conversationId"] = notification.DestinationEntityId.ToString("D");

            if (TryParseChatMessageId(notification.IdempotencyKey, out Guid messageId))
            {
                data["messageId"] = messageId.ToString("D");
            }
        }

        return data;
    }

    private static bool TryParseChatMessageId(string? idempotencyKey, out Guid messageId)
    {
        messageId = Guid.Empty;

        const string prefix = "chat-message:";

        if (string.IsNullOrEmpty(idempotencyKey) ||
            !idempotencyKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // chat-message:{messageId:N}:{recipientId:N} — the first segment is
        // the message id; the recipient segment is not needed here.
        string remainder = idempotencyKey.Substring(prefix.Length);
        int separator = remainder.IndexOf(':');

        return separator > 0 &&
            Guid.TryParseExact(
                remainder.Substring(0, separator),
                "N",
                out messageId) &&
            messageId != Guid.Empty;
    }
}
