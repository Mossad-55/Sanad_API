using MediatR;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.HelpRequests;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;
using Ahlena.Modules.Notifications.Application.Notifications;
namespace Ahlena.API.HelpRequestsIntegration;
public sealed class HelpRequestNotificationGateway(ISender sender) : IHelpRequestNotificationGateway
{ public async Task NotifyCreatedAsync(UserId userId, Guid elderlyId, Guid requestId, CancellationToken ct = default) { var result = await sender.Send(new CreateHelpRequestAlertNotificationsCommand(new ElderlyRecipient(userId, elderlyId), requestId, DateTime.UtcNow), ct); if (result.IsFailure) throw new InvalidOperationException(result.Error.Message); } }
