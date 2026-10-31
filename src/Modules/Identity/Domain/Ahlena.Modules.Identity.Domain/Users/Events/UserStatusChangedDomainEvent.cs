using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Users.Events;

public sealed record UserStatusChangedDomainEvent(
    UserId UserId,
    UserStatus PreviousStatus,
    UserStatus CurrentStatus,
    string? Reason)
    : DomainEvent;