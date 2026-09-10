using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

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

        user.ChangeNotificationPreferences(
            NotificationPreferences.Create(
                request.CheckInAlerts,
                request.MedicationReminders,
                request.BookingUpdates,
                request.CommunityNotifications),
            _dateTimeProvider.UtcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new NotificationPreferencesResponse(
            user.NotificationPreferences.CheckInAlerts,
            user.NotificationPreferences.MedicationReminders,
            user.NotificationPreferences.BookingUpdates,
            user.NotificationPreferences.CommunityNotifications);
    }
}
