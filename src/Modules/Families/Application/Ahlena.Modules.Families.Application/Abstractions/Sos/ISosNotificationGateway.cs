using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Application.Abstractions.Sos;

public interface ISosNotificationGateway
{
    Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid sosId, CancellationToken cancellationToken = default);
}
