using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

/// <summary>
/// Draft-only replacement of the full section list. Document type, audience,
/// and version stay as created; published and archived versions are
/// immutable.
/// </summary>
public sealed record UpdateLegalDocumentSectionsCommand(
    LegalDocumentId Id,
    IReadOnlyList<LegalSectionInput> Sections)
    : ICommand<LegalDocumentResponse>;
