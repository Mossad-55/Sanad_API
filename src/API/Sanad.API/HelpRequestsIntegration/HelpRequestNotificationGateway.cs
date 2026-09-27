using MediatR;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.HelpRequests;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;
namespace Sanad.API.HelpRequestsIntegration;
public sealed class HelpRequestNotificationGateway(ISender sender) : IHelpRequestNotificationGateway
{ public async Task NotifyCreatedAsync(UserId userId, Guid elderlyId, Guid requestId, CancellationToken ct = default) { var result = await sender.Send(new CreateHelpRequestAlertNotificationsCommand(new ElderlyRecipient(userId, elderlyId), requestId, DateTime.UtcNow), ct); if (result.IsFailure) throw new InvalidOperationException(result.Error.Message); } }
