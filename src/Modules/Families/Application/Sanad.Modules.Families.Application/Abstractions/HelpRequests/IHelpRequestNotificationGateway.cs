using Sanad.BuildingBlocks.Domain.Primitives.Ids;
namespace Sanad.Modules.Families.Application.Abstractions.HelpRequests;
public interface IHelpRequestNotificationGateway { Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid helpRequestId, CancellationToken cancellationToken = default); }
