using System.Text;
using Microsoft.Extensions.Options;

namespace Sanad.Modules.Identity.Infrastructure.Security;

public sealed class JwtOptionsValidator :
    IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        JwtOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(
            options.Issuer))
        {
            failures.Add(
                "Identity:Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(
            options.Audience))
        {
            failures.Add(
                "Identity:Jwt:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(
            options.SigningKey))
        {
            failures.Add(
                "Identity:Jwt:SigningKey is required.");
        }
        else if (Encoding.UTF8.GetByteCount(
                     options.SigningKey) < 32)
        {
            failures.Add(
                "Identity:Jwt:SigningKey must contain " +
                "at least 32 UTF-8 bytes.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                failures);
    }
}