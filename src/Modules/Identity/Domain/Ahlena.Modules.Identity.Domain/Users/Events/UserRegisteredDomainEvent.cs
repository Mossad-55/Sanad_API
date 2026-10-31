using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Users.Events;

public sealed record UserRegisteredDomainEvent(UserId UserId) : DomainEvent;