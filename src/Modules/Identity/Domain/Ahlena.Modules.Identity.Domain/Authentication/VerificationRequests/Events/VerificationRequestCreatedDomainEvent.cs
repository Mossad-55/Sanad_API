using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests.Events;

public sealed record VerificationRequestCreatedDomainEvent(
    VerificationRequestId VerificationRequestId)
    : DomainEvent;