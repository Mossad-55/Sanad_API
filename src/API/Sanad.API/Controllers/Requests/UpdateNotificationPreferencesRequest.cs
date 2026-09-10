namespace Sanad.API.Controllers.Requests;

public sealed record UpdateNotificationPreferencesRequest(
    bool CheckInAlerts,
    bool MedicationReminders,
    bool BookingUpdates,
    bool CommunityNotifications);
