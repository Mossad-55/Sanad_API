using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Legal;

public sealed record ListLegalDocumentsQuery(
    LegalDocumentType? DocumentType,
    LegalAudience? Audience,
    LegalDocumentStatus? Status)
    : IQuery<IReadOnlyList<LegalDocumentResponse>>;
