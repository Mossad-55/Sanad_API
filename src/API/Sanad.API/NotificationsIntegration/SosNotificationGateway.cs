using MediatR;
using Sanad.Modules.Families.Application.Abstractions.Sos;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.API.NotificationsIntegration;

public sealed class SosNotificationGateway(ISender sender) : ISosNotificationGateway
{
    public Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid sosId, CancellationToken cancellationToken = default) =>
        sender.Send(new CreateSosAlertNotificationsCommand(new ElderlyRecipient(elderlyIdentityUserId, elderlyId), sosId, DateTime.UtcNow), cancellationToken);
}
