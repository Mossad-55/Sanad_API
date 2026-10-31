using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed class GetMyNotificationPreferencesQueryHandler :
    IQueryHandler<
        GetMyNotificationPreferencesQuery,
        NotificationPreferencesResponse>
{
    private readonly IIdentityDbContext _dbContext;

    public GetMyNotificationPreferencesQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<NotificationPreferencesResponse>> Handle(
        GetMyNotificationPreferencesQuery request,
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
