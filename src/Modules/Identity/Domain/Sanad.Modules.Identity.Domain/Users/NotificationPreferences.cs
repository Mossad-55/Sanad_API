using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.Identity.Domain.Users;

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
        bool communityNotifications)
    {
        CheckInAlerts = checkInAlerts;
        MedicationReminders = medicationReminders;
        BookingUpdates = bookingUpdates;
        CommunityNotifications = communityNotifications;
    }

    public bool CheckInAlerts { get; private set; }

    public bool MedicationReminders { get; private set; }

    public bool BookingUpdates { get; private set; }

    public bool CommunityNotifications { get; private set; }

    public static NotificationPreferences Create(
        bool checkInAlerts,
        bool medicationReminders,
        bool bookingUpdates,
        bool communityNotifications)
    {
        return new NotificationPreferences(
            checkInAlerts,
            medicationReminders,
            bookingUpdates,
            communityNotifications);
    }

    public static NotificationPreferences CreateDefault()
    {
        return Create(
            checkInAlerts: true,
            medicationReminders: true,
            bookingUpdates: true,
            communityNotifications: true);
    }

    protected override IEnumerable<object?>
        GetEqualityComponents()
    {
        yield return CheckInAlerts;

        yield return MedicationReminders;

        yield return BookingUpdates;

        yield return CommunityNotifications;
    }
}