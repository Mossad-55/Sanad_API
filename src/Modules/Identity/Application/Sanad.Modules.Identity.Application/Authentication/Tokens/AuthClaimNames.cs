namespace Sanad.Modules.Identity.Application.Authentication.Tokens;

public static class AuthClaimNames
{
    public const string AccessType =
        "access_type";

    public const string AccountType =
        "account_type";

    /// <summary>
    /// Session-bound Normal access tokens carry the issuing device-session id.
    /// Restricted-verification tokens never carry it. Legacy Normal tokens
    /// issued before session binding have no <c>sid</c> claim.
    /// </summary>
    public const string SessionId =
        "sid";
}