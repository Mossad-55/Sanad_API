using Sanad.BuildingBlocks.Domain.Abstractions;

namespace Sanad.Modules.Identity.Domain.Users.Events;

public sealed record UserRegisteredDomainEvent(Guid UserId) : DomainEvent;