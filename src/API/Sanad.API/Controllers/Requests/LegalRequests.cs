using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.API.Controllers.Requests;

/// <summary>
/// SET-12 admin legal content requests. Enums bind from their numeric
/// values; version and status are never client-supplied.
/// </summary>
public sealed record CreateLegalDocumentRequest(
    LegalDocumentType DocumentType,
    LegalAudience Audience,
    IReadOnlyList<LegalSectionRequest> Sections);

public sealed record UpdateLegalDocumentSectionsRequest(
    IReadOnlyList<LegalSectionRequest> Sections);

public sealed record LegalSectionRequest(
    LegalSectionType SectionType,
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    IReadOnlyList<string>? ArabicBullets,
    IReadOnlyList<string>? EnglishBullets);
