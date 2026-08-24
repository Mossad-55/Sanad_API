namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class ExternalIdentityProviderOptions
{
    public const string SectionName =
        "Identity:ExternalProviders";

    public ExternalIdentityProviderSettings Google
    {
        get;
        init;
    } = new();

    public ExternalIdentityProviderSettings Apple
    {
        get;
        init;
    } = new();
}