namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// Shape rules that span the whole document (not a single section):
/// at least one section, a bounded section count, unique positive display
/// order, and the UserRights rule per document type. A Privacy Policy
/// version must contain exactly one UserRights section; a Terms &amp;
/// Conditions version must never contain one. Content itself always comes
/// from the CMS — nothing here synthesizes legal text.
/// </summary>
public static class LegalDocumentShape
{
    public static IReadOnlyList<string> Validate(
        LegalDocumentType documentType,
        IReadOnlyList<LegalSectionDraft> sections)
    {
        List<string> errors = [];

        if (sections is null || sections.Count == 0)
        {
            errors.Add("A legal document requires at least one section.");

            return errors;
        }

        if (sections.Count > LegalDocument.MaximumSectionCount)
        {
            errors.Add(
                "A legal document cannot contain more than " +
                $"{LegalDocument.MaximumSectionCount} sections.");
        }

        List<int> displayOrders = [];

        foreach (LegalSectionDraft section in sections)
        {
            if (section.DisplayOrder < 1)
            {
                errors.Add(
                    "Section display order must be a positive number.");
            }
            else
            {
                displayOrders.Add(section.DisplayOrder);
            }

            if (!Enum.IsDefined(section.SectionType))
            {
                errors.Add("Section type is not a supported value.");
            }
        }

        if (displayOrders.Distinct().Count() != displayOrders.Count)
        {
            errors.Add(
                "Section display order must be unique within a document.");
        }

        int userRightsCount =
            sections.Count(
                section =>
                    section.SectionType == LegalSectionType.UserRights);

        if (documentType == LegalDocumentType.PrivacyPolicy)
        {
            if (userRightsCount != 1)
            {
                errors.Add(
                    "A privacy policy requires exactly one UserRights " +
                    "section.");
            }
            else
            {
                LegalSectionDraft userRights =
                    sections.First(
                        section =>
                            section.SectionType ==
                            LegalSectionType.UserRights);

                if (userRights.ArabicBullets is null ||
                    userRights.ArabicBullets.Count == 0)
                {
                    errors.Add(
                        "The UserRights section requires at least one " +
                        "Arabic bullet.");
                }

                if (userRights.EnglishBullets is null ||
                    userRights.EnglishBullets.Count == 0)
                {
                    errors.Add(
                        "The UserRights section requires at least one " +
                        "English bullet.");
                }
            }
        }
        else if (userRightsCount > 0)
        {
            errors.Add(
                "A terms and conditions document cannot contain a " +
                "UserRights section.");
        }

        return errors;
    }
}
