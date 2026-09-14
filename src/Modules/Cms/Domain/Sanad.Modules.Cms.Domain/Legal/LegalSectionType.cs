namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// A Privacy Policy version carries exactly one UserRights section;
/// a Terms &amp; Conditions version never carries one.
/// </summary>
public enum LegalSectionType
{
    Text = 1,
    UserRights = 2
}
