using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

/// <summary>
/// Application-level section payload for legal document commands.
/// </summary>
public sealed record LegalSectionInput(
    LegalSectionType SectionType,
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    string ArabicDescription,
    string EnglishDescription,
    IReadOnlyList<string>? ArabicBullets,
    IReadOnlyList<string>? EnglishBullets)
{
    public LegalSectionDraft ToDraft()
    {
        return new LegalSectionDraft(
            SectionType,
            DisplayOrder,
            ArabicTitle,
            EnglishTitle,
            ArabicDescription,
            EnglishDescription,
            ArabicBullets,
            EnglishBullets);
    }

    public static IReadOnlyList<LegalSectionDraft> ToDrafts(
        IReadOnlyList<LegalSectionInput> sections)
    {
        return sections
            .Select(section => section.ToDraft())
            .ToList();
    }
}
