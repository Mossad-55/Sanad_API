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
    Elderly = 4,
    CareHomeOwner = 5
}

public static class LegalAudienceExtensions
{
    /// <summary>
    /// Legal content audiences, including the Care Home Owner audience added
    /// by E02. Help Center/FAQ code must use <see cref="IsHelpAudience"/>
    /// instead so Owner does not gain Help Center access.
    /// </summary>
    public static bool IsDefined(
        this LegalAudience audience)
    {
        return audience is
            LegalAudience.Family or
            LegalAudience.MedicalCaregiver or
            LegalAudience.CompanionCaregiver or
            LegalAudience.Elderly or
            LegalAudience.CareHomeOwner;
    }

    public static bool IsHelpAudience(
        this LegalAudience audience)
    {
        return audience is
            LegalAudience.Family or
            LegalAudience.MedicalCaregiver or
            LegalAudience.CompanionCaregiver or
            LegalAudience.Elderly;
    }
}
