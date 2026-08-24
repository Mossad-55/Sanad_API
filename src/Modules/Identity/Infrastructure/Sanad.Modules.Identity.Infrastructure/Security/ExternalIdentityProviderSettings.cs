namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class ExternalIdentityProviderSettings
{
    public bool Enabled
    {
        get;
        init;
    }

    public string[] Audiences
    {
        get;
        init;
    } = [];
}