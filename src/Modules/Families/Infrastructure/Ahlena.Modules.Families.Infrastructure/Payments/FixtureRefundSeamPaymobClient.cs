using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Families.Application.Abstractions.Payments;

namespace Ahlena.Modules.Families.Infrastructure.Payments;

/// <summary>
/// The fixture-only refund-outcome seam: an <see cref="IPaymobClient"/>
/// decorator registered in front of the real client that intercepts ONLY
/// refund calls whose provider transaction id is explicitly allowlisted in
/// <see cref="FixtureRefundSeamOptions.RefundOutcomes"/>, and returns the
/// same result shape the real client would have returned for that outcome —
/// <c>Paymob.RefundRejected</c> (definitive refusal → 502, claim released /
/// Failed), <c>Paymob.RefundOutcomeUnknown</c> (unconfirmable → 409,
/// ambiguity marker / Pending) or a confirmed completion (the normal success
/// path with a refund reference). No caller classification changes; no money
/// code is invented.
/// <para>
/// Behaviour contract:
/// * Default OFF — an absent configuration section or <c>Enabled=false</c>
/// makes this decorator a strict pass-through for EVERY call, including
/// refunds for ids that appear in the allowlist map.
/// * LIVE configuration — options are read through
/// <see cref="IOptionsMonitor{FixtureRefundSeamOptions}.CurrentValue"/> on
/// EVERY refund call, so a configuration reload (e.g. an edited
/// appsettings.json) is honoured without a restart. A reload that flips the
/// seam off, legitimately changes an id's outcome or corrupts a value takes
/// effect on the next call.
/// * Allowlist, not a global switch — an id absent from the map delegates to
/// the real client even when the seam is enabled.
/// * FAIL CLOSED on unreadable options — the registered
/// <see cref="FixtureRefundSeamOptionsValidator"/> is enforced by the options
/// pipeline on EVERY monitored-value creation, including right after a
/// reload. If the CURRENT configuration is invalid (a malformed outcome
/// value, an enabled seam with an empty allowlist, or an enablement attempt
/// under Production), <see cref="IOptionsMonitor{FixtureRefundSeamOptions}.CurrentValue"/>
/// throws <see cref="OptionsValidationException"/>; the allowlist itself is
/// then unknowable, so EVERY refund call is refused with
/// <c>Paymob.RefundOutcomeUnknown</c> — the real client is never touched
/// until the configuration is corrected. The reload change-callback may
/// surface the same validation exception to whoever triggers the change;
/// the decorator tolerates that explicitly (it logs and keeps running).
/// * Fail safe on values that ARE readable — if the current configured value
/// for an allowlisted id does not parse (possible only in a container
/// without the validator; the validator refuses such values at startup and
/// on every reload), the refund is refused with the same unconfirmable
/// shape. Parsed per call, never cached.
/// * Non-refund operations always delegate untouched — they never consult
/// the seam options.
/// * Startup validation (<see cref="FixtureRefundSeamOptionsValidator"/>,
/// <c>ValidateOnStart</c>) refuses enabling the seam in Production, refuses
/// an empty allowlist while enabled, and rejects malformed outcome values
/// even while the seam is disabled. The Production guard is absolute: a
/// later reload cannot enable the seam under Production, because the
/// re-validated options refuse and the decorator fails closed.
/// </para>
/// <para>
/// This seam exists so rehearsal fixture rows that represent money states a
/// real provider call would produce (Failed refunds, ambiguous refunds,
/// awaiting-execution and ambiguous duplicate-charge claims) can be provisioned
/// and re-derived without any provider network call. The startup/change log
/// line (enabled flag, allowlist count, sorted ids) is the operator's
/// checkable evidence that the running process actually carries the
/// configuration the provisioner's <c>--seam-deployed</c> declaration relies
/// on.
/// </para>
/// </summary>
public sealed class FixtureRefundSeamPaymobClient : IPaymobClient, IDisposable
{
    /// <summary>Prefix of the synthetic refund reference this seam's scripted
    /// <see cref="FixtureRefundOutcome.Completed"/> outcome returns. It is not a
    /// provider reference, so it must never be sent to a provider readback.</summary>
    public const string SyntheticRefundReferencePrefix = "fixture-refund-";

    // One concise, credential-free log line per intercepted refund call.
    private static readonly Action<ILogger, string, string, Exception?> LogInterceptedRefund =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "FixtureRefundIntercepted"),
            "Fixture refund seam intercepted refund for allowlisted fixture transaction {TransactionId} with scripted outcome {Outcome} (fixture-only; no provider call was made).");

    // Detailed state line (enabled flag, entry count, sorted allowlisted ids —
    // synthetic fixture ids, never secrets) so the running process's actual
    // seam configuration is observable in the log.
    private static readonly Action<ILogger, bool, int, string, Exception?> LogSeamState =
        LoggerMessage.Define<bool, int, string>(
            LogLevel.Information,
            new EventId(2, "FixtureRefundSeamState"),
            "Fixture refund seam state: enabled={Enabled}, allowlist entries={EntryCount}, allowlisted transaction ids=[{AllowlistedIds}] (fixture-only seam; synthetic fixture ids, no provider call).");

    private readonly IPaymobClient _inner;
    private readonly IOptionsMonitor<FixtureRefundSeamOptions> _optionsMonitor;
    private readonly ILogger<FixtureRefundSeamPaymobClient> _logger;
    private readonly IDisposable? _optionsChangeRegistration;

    public FixtureRefundSeamPaymobClient(
        IPaymobClient inner,
        IOptionsMonitor<FixtureRefundSeamOptions> optionsMonitor,
        ILogger<FixtureRefundSeamPaymobClient> logger)
    {
        _inner = inner;
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        LogState("startup");

        _optionsChangeRegistration = optionsMonitor.OnChange(
            _ => LogState("reload"));
    }

    /// <summary>
    /// One observable state line at construction and on every options change:
    /// enabled/disabled, allowlist entry count, sorted allowlisted ids. This
    /// is the gate evidence: the provisioner's --seam-deployed declaration
    /// must be backed by this line reporting Enabled=true with EXACTLY the
    /// ids the plan manifest printed. Withheld (with the invalid-options
    /// error already logged) whenever the monitored options are unreadable.
    /// Never throws — an invalid configuration must not break the change
    /// callback.
    /// </summary>
    private void LogState(string trigger)
    {
        if (!TryReadOptions(out FixtureRefundSeamOptions? current) || current is null)
        {
            return;
        }

        string sortedIds = string.Join(
            ",", current.RefundOutcomes.Keys.OrderBy(id => id, StringComparer.Ordinal));
        LogSeamState(_logger, current.Enabled, current.RefundOutcomes.Count, sortedIds, null);
    }

    /// <summary>
    /// Reads the CURRENT monitored options, fail-closed: with the registered
    /// <see cref="FixtureRefundSeamOptionsValidator"/> the options pipeline
    /// validates every (re)creation of the monitored value, so an invalid
    /// configuration surfaces as <see cref="OptionsValidationException"/> —
    /// logged here and reported as unreadable rather than crashing the
    /// caller.
    /// </summary>
    private bool TryReadOptions(out FixtureRefundSeamOptions? options)
    {
        try
        {
            options = _optionsMonitor.CurrentValue;
            return true;
        }
        catch (OptionsValidationException exception)
        {
            _logger.LogError(
                "Fixture refund seam options are INVALID in the current configuration (typically a reload after startup); the seam FAILS CLOSED: refund calls are refused with Paymob.RefundOutcomeUnknown and no provider call is made until the configuration is corrected. Validation failures: {ValidationFailures}",
                exception.Message);

            options = null;
            return false;
        }
    }

    public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
        PaymobPaymentIntentInput input,
        CancellationToken cancellationToken = default)
        => _inner.CreatePaymentIntentAsync(input, cancellationToken);

    public Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
        PaymobSubscriptionPaymentIntentInput input,
        CancellationToken cancellationToken = default)
        => _inner.CreateSubscriptionPaymentIntentAsync(input, cancellationToken);

    public Task<Result> UpdateSubscriptionAmountAsync(
        string providerSubscriptionId,
        decimal targetRecurringGross,
        CancellationToken cancellationToken = default)
        => _inner.UpdateSubscriptionAmountAsync(providerSubscriptionId, targetRecurringGross, cancellationToken);

    public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
        string refundTransactionId,
        CancellationToken cancellationToken = default)
    {
        // A synthetic fixture refund reference is produced only by this seam's
        // own scripted Completed outcome. It proves nothing about a provider, so
        // it is refused explicitly and NEVER falls through to the real client's
        // provider readback. Any other (real) id is delegated read-only, so a
        // genuine provider refund can still be verified through this decorator.
        if (!string.IsNullOrWhiteSpace(refundTransactionId)
            && refundTransactionId.StartsWith(SyntheticRefundReferencePrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "A synthetic fixture refund cannot be verified against the provider.")));
        }

        return _inner.VerifyRefundTransactionAsync(refundTransactionId, cancellationToken);
    }

    public async Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        // FAIL CLOSED first: if the monitored options cannot be re-validated
        // (an invalid reload after startup), the allowlist itself is
        // unknowable — no refund can be proven safe to send, so EVERY refund
        // call is refused with the unconfirmable shape and the real client
        // is never touched. Non-refund operations below are unaffected.
        if (!TryReadOptions(out FixtureRefundSeamOptions? options) || options is null)
        {
            return RefuseInvalidOptions();
        }

        // LIVE read: the refund call observes the current configuration, so
        // a reload is honoured on the next call. Pass-through remains the
        // default contract: the seam only acts when it is explicitly enabled
        // AND the id is explicitly allowlisted.
        if (!options.Enabled
            || string.IsNullOrWhiteSpace(paymobTransactionId)
            || !options.RefundOutcomes.TryGetValue(paymobTransactionId, out string? configuredOutcome))
        {
            return await _inner.RefundPaymentAsync(paymobTransactionId, amount, cancellationToken);
        }

        // Defense-in-depth fail safe (reachable only in a container without
        // the validator — the validator refuses malformed values at startup
        // and on every reload), parsed PER CALL, never cached: an
        // unparseable CURRENT value refuses the refund instead of guessing
        // and never sends the scripted fixture id to the real provider.
        if (!FixtureRefundOutcomeParser.TryParse(configuredOutcome, out FixtureRefundOutcome outcome))
        {
            return RefuseConfiguredValue(paymobTransactionId, configuredOutcome);
        }

        LogInterceptedRefund(_logger, paymobTransactionId, outcome.ToString(), null);

        return outcome switch
        {
            // The exact refusal shape a real 4xx produces (see
            // PaymobClient.RefundPaymentAsync): 502 mapping, claim release.
            FixtureRefundOutcome.Rejected => Result<string?>.Failure(
                new Error("Paymob.RefundRejected",
                    "Paymob refund request failed with scripted fixture outcome Rejected.")),

            // The exact unconfirmable shape a real 5xx/transport failure
            // produces: 409 mapping, ambiguity marker / Pending.
            FixtureRefundOutcome.Unknown => Result<string?>.Failure(
                new Error("Paymob.RefundOutcomeUnknown",
                    "The refund outcome could not be confirmed.")),

            // The same success path as a real 2xx refund: a refund
            // transaction reference string. Deterministic and derived only
            // from the fixture id.
            FixtureRefundOutcome.Completed => Result<string?>.Success(
                $"{SyntheticRefundReferencePrefix}{paymobTransactionId}"),

            _ => RefuseConfiguredValue(paymobTransactionId, configuredOutcome),
        };
    }

    private Result<string?> RefuseConfiguredValue(string paymobTransactionId, string? configuredOutcome)
    {
        _logger.LogError(
            "Fixture refund seam is enabled and transaction {TransactionId} has an unparseable configured outcome {ConfiguredOutcome}; the refund is refused without any provider call.",
            paymobTransactionId,
            configuredOutcome ?? "<null>");

        return Result<string?>.Failure(
            new Error("Paymob.RefundOutcomeUnknown",
                "The refund outcome could not be confirmed."));
    }

    private Result<string?> RefuseInvalidOptions()
    {
        return Result<string?>.Failure(
            new Error("Paymob.RefundOutcomeUnknown",
                "The refund outcome could not be confirmed."));
    }

    /// <summary>Releases the options-change registration.</summary>
    public void Dispose()
    {
        _optionsChangeRegistration?.Dispose();
    }
}
