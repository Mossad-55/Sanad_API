using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record UpdateMyNotificationPreferencesCommand(
    UserId CurrentUserId,
    bool CheckInAlerts,
    bool MedicationReminders,
    bool BookingUpdates,
    bool CommunityNotifications,
    bool FamilyActivityAlerts,
    bool NewOrders,
    bool MessagesFromFamilies,
    bool SystemNotifications,
    bool? HelpRequestAlerts,
    bool? ChatMessages = null)
    : ICommand<NotificationPreferencesResponse>;
