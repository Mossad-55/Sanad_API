namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// Raw section payload used by the domain factory and by the shared shape
/// validation. Kept in the domain so admin validation, handler guards, and
/// tests all judge the same closed rule set.
/// </summary>
public readonly record struct LegalSectionDraft(
    LegalSectionType SectionType,
    int DisplayOrder,
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicDescription,
    string? EnglishDescription,
    IReadOnlyList<string>? ArabicBullets,
    IReadOnlyList<string>? EnglishBullets);
