using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

public sealed record DeviceSessionRefreshTokenReuseDetectedDomainEvent(
        DeviceSessionId DeviceSessionId,
        UserId UserId)
    : DomainEvent;