using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Application.Abstractions.Sos;

public interface ISosNotificationGateway
{
    Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid sosId, CancellationToken cancellationToken = default);
}
