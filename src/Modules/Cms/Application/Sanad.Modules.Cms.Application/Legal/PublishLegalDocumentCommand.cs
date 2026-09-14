using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

/// <summary>
/// Archives the current published version of the same (type, audience) pair
/// (when present) and publishes the requested draft in one SaveChanges
/// operation, so exactly one Published version exists per pair.
/// </summary>
public sealed record PublishLegalDocumentCommand(
    LegalDocumentId Id)
    : ICommand<LegalDocumentResponse>;
