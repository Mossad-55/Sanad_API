using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed record LegalDocumentResponse(
    LegalDocumentId Id,
    LegalDocumentType DocumentType,
    LegalAudience Audience,
    int Version,
    LegalDocumentStatus Status,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc,
    DateTime? PublishedOnUtc,
    IReadOnlyList<LegalSectionResponse> Sections);

public sealed record LegalSectionResponse(
    LegalSectionType SectionType,
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    IReadOnlyList<string> ArabicBullets,
    IReadOnlyList<string> EnglishBullets);
