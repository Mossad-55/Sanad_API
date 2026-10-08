using Microsoft.Extensions.Options;

namespace Sanad.Modules.Caregivers.Infrastructure.Security;

public sealed class PayoutIbanProtectionOptionsValidator :
    IValidateOptions<PayoutIbanProtectionOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        PayoutIbanProtectionOptions options)
    {
        // Absent configuration is valid at startup: the protector stays
        // dormant and payout-account writes fail closed at use time.
        if (!options.IsConfigured)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];

        if (!options.Keys.TryGetValue(
                options.CurrentKeyId,
                out string? currentKey) ||
            !IsValidKey(currentKey))
        {
            failures.Add(
                "Caregivers:PayoutIbanProtection:CurrentKeyId must reference " +
                "a configured 256-bit base64 key.");
        }

        foreach ((string keyId, string keyValue) in options.Keys)
        {
            if (string.IsNullOrWhiteSpace(keyId) ||
                keyId.Contains('.') ||
                keyId.Any(char.IsWhiteSpace))
            {
                failures.Add(
                    "Payout IBAN protection key ids must be non-empty " +
                    "and contain no dots or whitespace.");
                break;
            }

            if (!IsValidKey(keyValue))
            {
                failures.Add(
                    "Payout IBAN protection keys must be 256-bit " +
                    "base64 values.");
                break;
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                failures);
    }

    private static bool IsValidKey(
        string keyValue)
    {
        if (string.IsNullOrWhiteSpace(keyValue))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(keyValue).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
