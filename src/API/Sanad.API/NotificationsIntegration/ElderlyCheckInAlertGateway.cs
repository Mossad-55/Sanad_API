using MediatR;
using Sanad.Modules.Families.Application.Abstractions.Notifications;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.API.NotificationsIntegration;

public sealed class ElderlyCheckInAlertGateway(ISender sender) : IElderlyCheckInAlertGateway
{
    public async Task<int> CreateNegativeCheckInAlertsAsync(ElderlyCheckInAlertRequest elderly, DateTime createdOnUtc, CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(new CreateCheckInAlertNotificationsCommand(new ElderlyRecipient(new(elderly.ElderlyIdentityUserId), elderly.ElderlyEntityId), "ElderlyCheckIn", "NegativeCheckIn", "Daily check-in alert", "An elderly family member reported that they are not feeling well today.", createdOnUtc, elderly.LocalDate), cancellationToken);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error.Message);
    }
}
