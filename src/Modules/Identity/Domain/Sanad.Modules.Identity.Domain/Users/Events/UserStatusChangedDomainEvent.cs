using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Users.Events;

public sealed record UserStatusChangedDomainEvent(
    UserId UserId,
    UserStatus PreviousStatus,
    UserStatus CurrentStatus,
    string? Reason)
    : DomainEvent;