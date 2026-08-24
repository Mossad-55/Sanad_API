using Microsoft.Extensions.Options;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class ExternalIdentityProviderOptionsValidator :
    IValidateOptions<ExternalIdentityProviderOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ExternalIdentityProviderOptions options)
    {
        List<string> failures = [];

        ValidateProvider(
            options.Google,
            "Identity:ExternalProviders:Google",
            failures);

        ValidateProvider(
            options.Apple,
            "Identity:ExternalProviders:Apple",
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                failures);
    }

    private static void ValidateProvider(
        ExternalIdentityProviderSettings settings,
        string configurationPath,
        List<string> failures)
    {
        if (!settings.Enabled)
        {
            return;
        }

        if (settings.Audiences.Length == 0)
        {
            failures.Add(
                $"{configurationPath}:Audiences " +
                "must contain at least one audience.");

            return;
        }

        string[] normalizedAudiences =
            settings.Audiences
                .Where(audience =>
                    !string.IsNullOrWhiteSpace(
                        audience))
                .Select(audience =>
                    audience.Trim())
                .ToArray();

        if (normalizedAudiences.Length !=
            settings.Audiences.Length)
        {
            failures.Add(
                $"{configurationPath}:Audiences " +
                "cannot contain an empty audience.");
        }

        if (normalizedAudiences
                .Distinct(
                    StringComparer.Ordinal)
                .Count() !=
            normalizedAudiences.Length)
        {
            failures.Add(
                $"{configurationPath}:Audiences " +
                "cannot contain duplicate audiences.");
        }
    }
}