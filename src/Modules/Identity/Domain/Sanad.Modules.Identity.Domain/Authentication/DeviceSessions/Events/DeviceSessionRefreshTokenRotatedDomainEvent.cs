using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

public sealed record DeviceSessionRefreshTokenRotatedDomainEvent(
        DeviceSessionId DeviceSessionId,
        int RotationCount,
        DateTime ExpiresOnUtc)
    : DomainEvent;