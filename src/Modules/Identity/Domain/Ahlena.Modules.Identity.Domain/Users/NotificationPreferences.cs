using Ahlena.BuildingBlocks.Domain.Abstractions;

namespace Ahlena.Modules.Identity.Domain.Users;

public sealed class NotificationPreferences :
    ValueObject
{
    private NotificationPreferences()
    {
    }

    private NotificationPreferences(
        bool checkInAlerts,
        bool medicationReminders,
        bool bookingUpdates,
        bool communityNotifications,
        bool familyActivityAlerts,
        bool newOrders,
        bool messagesFromFamilies,
        bool systemNotifications,
        bool helpRequestAlerts = true,
        bool? chatMessages = null)
    {
        CheckInAlerts = checkInAlerts;
        MedicationReminders = medicationReminders;
        BookingUpdates = bookingUpdates;
        CommunityNotifications = communityNotifications;
        FamilyActivityAlerts = familyActivityAlerts;
        NewOrders = newOrders;
        MessagesFromFamilies = messagesFromFamilies;
        SystemNotifications = systemNotifications;
        HelpRequestAlerts = helpRequestAlerts;
        // Backward-compatible rollout: legacy rows adopt the existing
        // MessagesFromFamilies opt-out; new users start enabled.
        ChatMessages = chatMessages ?? messagesFromFamilies;
    }

    public bool CheckInAlerts { get; private set; }

    public bool MedicationReminders { get; private set; }

    public bool BookingUpdates { get; private set; }

    public bool CommunityNotifications { get; private set; }

    public bool FamilyActivityAlerts { get; private set; }

    public bool NewOrders { get; private set; }

    public bool MessagesFromFamilies { get; private set; }

    public bool SystemNotifications { get; private set; }
    public bool HelpRequestAlerts { get; private set; }

    /// <summary>
    /// Chat Plus canonical chat-push preference for all sender types
    /// (default enabled). Consulted by chat push dispatch; the legacy
    /// <see cref="MessagesFromFamilies"/> field is preserved for its
    /// existing consumers and mirrored only through the documented
    /// compatibility rule.
    /// </summary>
    public bool ChatMessages { get; private set; }

    public static NotificationPreferences Create(
        bool checkInAlerts,
        bool medicationReminders,
        bool bookingUpdates,
        bool communityNotifications,
        bool familyActivityAlerts,
        bool newOrders,
        bool messagesFromFamilies,
        bool systemNotifications,
        bool helpRequestAlerts = true,
        bool? chatMessages = null)
    {
        return new NotificationPreferences(
            checkInAlerts,
            medicationReminders,
            bookingUpdates,
            communityNotifications,
            familyActivityAlerts,
            newOrders,
            messagesFromFamilies,
            systemNotifications, helpRequestAlerts, chatMessages);
    }

    public static NotificationPreferences CreateDefault()
    {
        return Create(
            checkInAlerts: true,
            medicationReminders: true,
            bookingUpdates: true,
            communityNotifications: true,
            familyActivityAlerts: true,
            newOrders: true,
            messagesFromFamilies: true,
            systemNotifications: true);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return CheckInAlerts;

        yield return MedicationReminders;

        yield return BookingUpdates;

        yield return CommunityNotifications;

        yield return FamilyActivityAlerts;

        yield return NewOrders;

        yield return MessagesFromFamilies;

        yield return SystemNotifications;
        yield return HelpRequestAlerts;
        yield return ChatMessages;
    }
}
