using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed class UpdateMyNotificationPreferencesCommandHandler :
    ICommandHandler<
        UpdateMyNotificationPreferencesCommand,
        NotificationPreferencesResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateMyNotificationPreferencesCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<NotificationPreferencesResponse>> Handle(
        UpdateMyNotificationPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.CurrentUserId,
                    cancellationToken);

        if (user is null)
        {
            return Result<NotificationPreferencesResponse>.Failure(
                AccountErrors.UserNotFound);
        }

        if (user.Status ==
            UserStatus.Blocked)
        {
            return Result<NotificationPreferencesResponse>.Failure(
                AccountErrors.InvalidOperation);
        }

        // Backward-compatible full replacement: explicit chatMessages
        // wins; if omitted and messagesFromFamilies changes from its stored
        // value, mirror that legacy change to chatMessages; if neither
        // changes, preserve chatMessages. Omission alone never resets an
        // opt-out.
        bool storedChatMessages =
            user.NotificationPreferences.ChatMessages;
        bool legacyChanged =
            request.MessagesFromFamilies !=
            user.NotificationPreferences.MessagesFromFamilies;
        bool resolvedChatMessages =
            request.ChatMessages
            ?? (legacyChanged
                ? request.MessagesFromFamilies
                : storedChatMessages);

        user.ChangeNotificationPreferences(
            NotificationPreferences.Create(
                request.CheckInAlerts,
                request.MedicationReminders,
                request.BookingUpdates,
                request.CommunityNotifications,
                request.FamilyActivityAlerts,
                request.NewOrders,
                request.MessagesFromFamilies,
                request.SystemNotifications,
                request.HelpRequestAlerts ?? user.NotificationPreferences.HelpRequestAlerts,
                resolvedChatMessages),
            _dateTimeProvider.UtcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new NotificationPreferencesResponse(
            user.NotificationPreferences.CheckInAlerts,
            user.NotificationPreferences.MedicationReminders,
            user.NotificationPreferences.BookingUpdates,
            user.NotificationPreferences.CommunityNotifications,
            user.NotificationPreferences.FamilyActivityAlerts,
            user.NotificationPreferences.NewOrders,
            user.NotificationPreferences.MessagesFromFamilies,
            user.NotificationPreferences.SystemNotifications,
            user.NotificationPreferences.HelpRequestAlerts,
            user.NotificationPreferences.ChatMessages);
    }
}
