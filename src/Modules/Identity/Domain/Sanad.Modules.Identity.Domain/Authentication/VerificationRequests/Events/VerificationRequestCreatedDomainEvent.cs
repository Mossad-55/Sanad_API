using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.Events;

public sealed record VerificationRequestCreatedDomainEvent(
    VerificationRequestId VerificationRequestId)
    : DomainEvent;