namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// Legal content audiences. Values 1–5 are the five normal app audiences,
/// which the app reads derive from the JWT <c>account_type</c> claims; there is
/// no legal or help audience for the admin account types.
/// <para>
/// <see cref="Public"/> (0) is the anonymous, login-free audience added by
/// T9-B/P1 and served only by the public legal route. It is never derived from
/// a token and is never served on the authenticated app routes, so its value
/// must stay outside the token-derived 1–5 range. Values 1–5 keep their
/// original numbers and meaning (the column stores the enum as <c>int</c>).
/// </para>
/// </summary>
public enum LegalAudience
{
    Public = 0,
    Family = 1,
    MedicalCaregiver = 2,
    CompanionCaregiver = 3,
    Elderly = 4,
    CareHomeOwner = 5
}

public static class LegalAudienceExtensions
{
    /// <summary>
    /// Defines the audiences that can be authored and read, including the
    /// anonymous <see cref="LegalAudience.Public"/> audience (T9-B) and the
    /// Care Home Owner audience added by E02. Help Center/FAQ code uses
    /// <see cref="IsHelpAudience"/> instead, which excludes Public.
    /// </summary>
    public static bool IsDefined(
        this LegalAudience audience)
    {
        return audience is
            LegalAudience.Public or
            LegalAudience.Family or
            LegalAudience.MedicalCaregiver or
            LegalAudience.CompanionCaregiver or
            LegalAudience.Elderly or
            LegalAudience.CareHomeOwner;
    }

    /// <summary>
    /// Help Center audiences. Public is deliberately excluded: the Help Center
    /// stays authenticated (T9-B decision 6A's minimal public set is the
    /// published Privacy Policy and Terms &amp; Conditions only).
    /// </summary>
    public static bool IsHelpAudience(
        this LegalAudience audience)
    {
        return audience is
            LegalAudience.Family or
            LegalAudience.MedicalCaregiver or
            LegalAudience.CompanionCaregiver or
            LegalAudience.Elderly or
            LegalAudience.CareHomeOwner;
    }
}
