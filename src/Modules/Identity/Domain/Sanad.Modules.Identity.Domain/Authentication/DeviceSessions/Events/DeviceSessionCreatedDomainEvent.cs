using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Authentication.DeviceSessions.Events;

public sealed record DeviceSessionCreatedDomainEvent(
    DeviceSessionId DeviceSessionId)
    : DomainEvent;