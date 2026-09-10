using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record UpdateMyNotificationPreferencesCommand(
    UserId CurrentUserId,
    bool CheckInAlerts,
    bool MedicationReminders,
    bool BookingUpdates,
    bool CommunityNotifications)
    : ICommand<NotificationPreferencesResponse>;
