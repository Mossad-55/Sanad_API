using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Domain.Users.Events;

public sealed record UserExternalLoginUnlinkedDomainEvent(
    UserId UserId,
    ExternalLoginProvider Provider)
    : DomainEvent;