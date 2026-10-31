namespace Ahlena.API.Controllers.Requests;

public sealed record UpdateNotificationPreferencesRequest(
    bool CheckInAlerts,
    bool MedicationReminders,
    bool BookingUpdates,
    bool CommunityNotifications,
    bool FamilyActivityAlerts,
    bool NewOrders,
    bool MessagesFromFamilies,
    bool SystemNotifications,
    bool? HelpRequestAlerts = null,
    bool? ChatMessages = null);
