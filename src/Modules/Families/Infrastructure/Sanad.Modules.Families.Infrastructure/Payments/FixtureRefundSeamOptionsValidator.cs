using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sanad.Modules.Families.Infrastructure.Payments;

/// <summary>
/// Fail-closed validation for <see cref="FixtureRefundSeamOptions"/>,
/// registered with <c>ValidateOnStart</c> so a bad seam configuration refuses
/// application start instead of guessing a money outcome at refund time.
/// <para>
/// Rules, in order of severity:
/// 1. The scripted outcome map is validated ALWAYS — even while the seam is
/// disabled — so a typo can never hide behind the off switch and silently
/// become live later.
/// 2. Enabling the seam in the Production environment is refused outright:
/// the seam exists for rehearsal fixtures only.
/// 3. Enabling the seam with an EMPTY allowlist is refused: an enabled seam
/// with zero allowlisted ids is a silent no-op that only pretends to be on.
/// 4. Enabling the seam without a resolvable host environment is refused:
/// without an environment we cannot prove this is not Production.
/// </para>
/// </summary>
public sealed class FixtureRefundSeamOptionsValidator : IValidateOptions<FixtureRefundSeamOptions>
{
    private readonly IHostEnvironment? _environment;

    public FixtureRefundSeamOptionsValidator(IServiceProvider serviceProvider)
    {
        // Optional on purpose: the validator must be constructible in any
        // container (including a bare test ServiceCollection); an absent
        // IHostEnvironment simply means the seam cannot be prove-ably enabled.
        _environment = serviceProvider.GetService<IHostEnvironment>();
    }

    public ValidateOptionsResult Validate(string? name, FixtureRefundSeamOptions options)
    {
        var failures = new List<string>();

        // The outcome map is validated even when the seam is disabled: a
        // malformed entry must never be allowed to wait for an enablement.
        foreach ((string transactionId, string rawOutcome) in options.RefundOutcomes)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                failures.Add(
                    $"{FixtureRefundSeamOptions.SectionName}:RefundOutcomes contains an empty transaction id key." +
                    " Every allowlist entry must name the fixture provider transaction id it scripts.");
                continue;
            }

            if (!FixtureRefundOutcomeParser.TryParse(rawOutcome, out _))
            {
                failures.Add(
                    $"{FixtureRefundSeamOptions.SectionName}:RefundOutcomes['{transactionId}'] = '{rawOutcome}'" +
                    " is not a valid outcome. Use Rejected, Unknown or Completed (case-insensitive)." +
                    " An unrecognized refund outcome is refused rather than guessed.");
            }
        }

        if (options.Enabled)
        {
            if (_environment is null)
            {
                failures.Add(
                    $"{FixtureRefundSeamOptions.SectionName}:Enabled=true requires a resolvable host environment." +
                    " The fixture refund seam is forbidden in Production and the environment could not be determined.");
            }
            else if (_environment.IsProduction())
            {
                failures.Add(
                    $"{FixtureRefundSeamOptions.SectionName}:Enabled=true is forbidden in the Production environment." +
                    " The fixture refund-outcome seam intercepts real refund calls with scripted results and must" +
                    " only run in a non-Production rehearsal environment.");
            }

            if (options.RefundOutcomes.Count == 0)
            {
                failures.Add(
                    $"{FixtureRefundSeamOptions.SectionName}:Enabled=true with an empty RefundOutcomes map is refused:" +
                    " an enabled seam without any allowlisted transaction id is a no-op that LOOKS on, which would" +
                    " make a --seam-deployed provisioning declaration a lie. Deploy the inert revision, THEN configure" +
                    " exactly the allowlisted ids printed by the provisioner's plan, THEN restart/reload and confirm" +
                    " the startup log line before provisioning.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
