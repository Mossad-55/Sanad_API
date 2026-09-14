using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

internal static class LegalDocumentMappings
{
    public static LegalDocumentResponse ToResponse(
        this LegalDocument document)
    {
        IReadOnlyList<LegalSectionResponse> sections =
            document.Sections
                .OrderBy(section => section.DisplayOrder)
                .Select(section => section.ToResponse())
                .ToList();

        return new LegalDocumentResponse(
            document.Id,
            document.DocumentType,
            document.Audience,
            document.Version,
            document.Status,
            document.CreatedOnUtc,
            document.UpdatedOnUtc,
            document.PublishedOnUtc,
            sections);
    }

    public static LegalSectionResponse ToResponse(
        this LegalSection section)
    {
        return new LegalSectionResponse(
            section.SectionType,
            section.DisplayOrder,
            section.ArabicTitle,
            section.EnglishTitle,
            section.ArabicDescription,
            section.EnglishDescription,
            section.ArabicBullets,
            section.EnglishBullets);
    }
}
