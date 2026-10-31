using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

public sealed record DeviceSessionRefreshTokenRotatedDomainEvent(
        DeviceSessionId DeviceSessionId,
        int RotationCount,
        DateTime ExpiresOnUtc)
    : DomainEvent;