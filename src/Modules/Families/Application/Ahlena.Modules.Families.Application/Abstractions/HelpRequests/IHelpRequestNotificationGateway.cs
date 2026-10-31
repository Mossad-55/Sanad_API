using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
namespace Ahlena.Modules.Families.Application.Abstractions.HelpRequests;
public interface IHelpRequestNotificationGateway { Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid helpRequestId, CancellationToken cancellationToken = default); }
