namespace Sanad.Modules.Families.Infrastructure.Payments;

/// <summary>
/// Which scripted result the fixture refund-outcome seam returns for an
/// allowlisted refund call. The three values map one-to-one onto the shapes
/// the REAL provider client can already return, so no caller classification
/// changes:
/// <list type="bullet">
/// <item><see cref="Rejected"/> — the definitive provider refusal
/// (<c>Paymob.RefundRejected</c>): nothing moved; the caller releases the
/// claim/Failed state and the HTTP mapping is 502.</item>
/// <item><see cref="Unknown"/> — the unconfirmable outcome
/// (<c>Paymob.RefundOutcomeUnknown</c>): the money may or may not have moved;
/// the caller raises the ambiguity marker/Pending state and the HTTP mapping
/// is 409. A retry is refused until reconciliation.</item>
/// <item><see cref="Completed"/> — a confirmed refund carrying the same
/// success shape (a refund transaction reference string) as a real 2xx.</item>
/// </list>
/// </summary>
public enum FixtureRefundOutcome
{
    Rejected,
    Unknown,
    Completed
}

/// <summary>
/// Configuration for the fixture-only refund-outcome seam
/// (<see cref="FixtureRefundSeamPaymobClient"/>). Default is OFF: an absent
/// section or <c>Enabled=false</c> makes the seam a pure pass-through with
/// zero behaviour change. There are no secrets here — only a map from fixture
/// transaction ids to a scripted outcome.
/// <para>
/// Configuration shape:
/// <code>
/// "FixtureRefundSeam": {
///   "Enabled": true,
///   "RefundOutcomes": {
///     "spf-r2-carehome-refund-failed-txn": "Rejected",
///     "spf-r2-caregiver-refund-failed-txn": "Rejected",
///     "spf-r2-seam-claim-approved-txn": "Rejected",
///     "spf-r2-seam-claim-ambiguous-txn": "Unknown"
///   }
/// }
/// </code>
/// The outcome values are validated at binding/startup (see
/// <see cref="FixtureRefundSeamOptionsValidator"/>); an unrecognized value
/// refuses application start rather than guessing a money outcome.
/// </para>
/// </summary>
public sealed class FixtureRefundSeamOptions
{
    public const string SectionName = "FixtureRefundSeam";

    /// <summary>
    /// Master switch. <c>false</c> (the default) is a strict pass-through:
    /// every <see cref="IPaymobClient"/> call — including refunds for ids that
    /// appear in <see cref="RefundOutcomes"/> — is delegated to the real
    /// client untouched.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The allowlist: fixture provider-transaction id (as stored on the
    /// fixture row and therefore passed to the refund call) to its scripted
    /// outcome. Only these ids are intercepted; every other refund call is
    /// delegated untouched. Keys must be non-empty ids; values must parse as
    /// one of the <see cref="FixtureRefundOutcome"/> names (case-insensitive).
    /// </summary>
    public Dictionary<string, string> RefundOutcomes { get; set; } =
        new(StringComparer.Ordinal);
}

/// <summary>Case-insensitive parsing of the configured outcome values.</summary>
public static class FixtureRefundOutcomeParser
{
    public static bool TryParse(string? value, out FixtureRefundOutcome outcome)
    {
        if (Enum.TryParse(
                value?.Trim(),
                ignoreCase: true,
                out outcome))
        {
            return Enum.IsDefined(outcome);
        }

        outcome = FixtureRefundOutcome.Rejected;
        return false;
    }
}
