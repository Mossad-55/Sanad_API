using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed record ListLegalDocumentsQuery(
    LegalDocumentType? DocumentType,
    LegalAudience? Audience,
    LegalDocumentStatus? Status)
    : IQuery<IReadOnlyList<LegalDocumentResponse>>;
