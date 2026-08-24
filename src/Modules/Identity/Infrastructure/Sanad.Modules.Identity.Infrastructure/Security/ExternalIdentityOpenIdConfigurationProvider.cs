using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class ExternalIdentityOpenIdConfigurationProvider :
    IExternalIdentityOpenIdConfigurationProvider
{
    private const string GoogleMetadataAddress =
        "https://accounts.google.com/" +
        ".well-known/openid-configuration";

    private const string AppleMetadataAddress =
        "https://appleid.apple.com/" +
        ".well-known/openid-configuration";

    private readonly ConfigurationManager<
        OpenIdConnectConfiguration>
        _googleConfigurationManager;

    private readonly ConfigurationManager<
        OpenIdConnectConfiguration>
        _appleConfigurationManager;

    public ExternalIdentityOpenIdConfigurationProvider()
    {
        var documentRetriever =
            new HttpDocumentRetriever
            {
                RequireHttps =
                    true
            };

        _googleConfigurationManager =
            CreateConfigurationManager(
                GoogleMetadataAddress,
                documentRetriever);

        _appleConfigurationManager =
            CreateConfigurationManager(
                AppleMetadataAddress,
                documentRetriever);
    }

    public Task<OpenIdConnectConfiguration> GetAsync(
        ExternalLoginProvider provider,
        CancellationToken cancellationToken)
    {
        return provider switch
        {
            ExternalLoginProvider.Google =>
                _googleConfigurationManager
                    .GetConfigurationAsync(
                        cancellationToken),

            ExternalLoginProvider.Apple =>
                _appleConfigurationManager
                    .GetConfigurationAsync(
                        cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Only Google and Apple are supported.")
        };
    }

    private static ConfigurationManager<
        OpenIdConnectConfiguration>
        CreateConfigurationManager(
            string metadataAddress,
            IDocumentRetriever documentRetriever)
    {
        return new ConfigurationManager<
            OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                documentRetriever);
    }
}