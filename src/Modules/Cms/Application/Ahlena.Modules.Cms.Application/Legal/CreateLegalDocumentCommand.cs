using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Legal;

public sealed record CreateLegalDocumentCommand(
    LegalDocumentType DocumentType,
    LegalAudience Audience,
    IReadOnlyList<LegalSectionInput> Sections)
    : ICommand<LegalDocumentResponse>;
