using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

/// <summary>
/// Current Published version for exactly one (type, audience) pair. The
/// audience is derived from the JWT by the controller — callers cannot
/// override it, and there is never a fallback to another audience or to
/// another document type.
/// </summary>
public sealed record GetPublishedLegalDocumentQuery(
    LegalDocumentType DocumentType,
    LegalAudience Audience)
    : IQuery<LegalDocumentResponse>;
