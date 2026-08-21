using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Users.Events;

public sealed record UserPasswordChangedDomainEvent(
    UserId UserId,
    PasswordChangeReason Reason)
    : DomainEvent;