using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Users.Events;

public sealed record UserContactVerifiedDomainEvent(
    UserId UserId,
    UserContactType ContactType)
    : DomainEvent;