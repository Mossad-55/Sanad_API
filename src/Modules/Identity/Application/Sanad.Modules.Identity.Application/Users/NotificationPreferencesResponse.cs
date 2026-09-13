namespace Sanad.Modules.Identity.Application.Users;

public sealed record NotificationPreferencesResponse(
    bool CheckInAlerts,
    bool MedicationReminders,
    bool BookingUpdates,
    bool CommunityNotifications,
    bool FamilyActivityAlerts,
    bool NewOrders,
    bool MessagesFromFamilies,
    bool SystemNotifications);
