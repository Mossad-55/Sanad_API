using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed record CreateLegalDocumentCommand(
    LegalDocumentType DocumentType,
    LegalAudience Audience,
    IReadOnlyList<LegalSectionInput> Sections)
    : ICommand<LegalDocumentResponse>;
