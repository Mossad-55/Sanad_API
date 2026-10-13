using Sanad.Modules.Notifications.Domain.Notifications;

namespace Sanad.Modules.Notifications.Application.Notifications;

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
        string deepLink = $"sanad://inbox?notificationId={Uri.EscapeDataString(notification.Id.ToString("D"))}";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["notificationId"] = notification.Id.ToString("D"),
            ["entityId"] = notification.DestinationEntityId.ToString("D"),
            ["entityKind"] = notification.DestinationEntityKind,
            ["deepLink"] = deepLink
        };
    }
}
