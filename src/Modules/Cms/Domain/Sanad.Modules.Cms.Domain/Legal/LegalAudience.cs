namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// The four normal app audiences. There is no legal or help audience for
/// SuperAdmin, ContentAdmin, or SupportAdmin.
/// </summary>
public enum LegalAudience
{
    Family = 1,
    MedicalCaregiver = 2,
    CompanionCaregiver = 3,
    Elderly = 4
}

public static class LegalAudienceExtensions
{
    public static bool IsDefined(
        this LegalAudience audience)
    {
        return audience is
            LegalAudience.Family or
            LegalAudience.MedicalCaregiver or
            LegalAudience.CompanionCaregiver or
            LegalAudience.Elderly;
    }
}
