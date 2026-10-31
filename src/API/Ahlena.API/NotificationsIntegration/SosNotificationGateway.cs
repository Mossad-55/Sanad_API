using MediatR;
using Ahlena.Modules.Families.Application.Abstractions.Sos;
using Ahlena.Modules.Notifications.Application.Notifications;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.API.NotificationsIntegration;

public sealed class SosNotificationGateway(ISender sender) : ISosNotificationGateway
{
    public Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid sosId, CancellationToken cancellationToken = default) =>
        sender.Send(new CreateSosAlertNotificationsCommand(new ElderlyRecipient(elderlyIdentityUserId, elderlyId), sosId, DateTime.UtcNow), cancellationToken);
}
