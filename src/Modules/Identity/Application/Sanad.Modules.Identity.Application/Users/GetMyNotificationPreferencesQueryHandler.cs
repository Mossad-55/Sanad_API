using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

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
            user.NotificationPreferences.CommunityNotifications);
    }
}
