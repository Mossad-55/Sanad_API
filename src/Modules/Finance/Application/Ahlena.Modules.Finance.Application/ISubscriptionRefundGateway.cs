namespace Ahlena.Modules.Finance.Application;

/// <summary>
/// The provider-agnostic refund gateway seam for duplicate-charge claims.
/// Finance persists and decides; the implementation lives with the purchasable
/// owner (Families.Infrastructure), which owns the provider client and the
/// charge-transaction resolution.
/// <para>
/// Preparation is deliberately a separate step from the refund itself, and it
/// happens BEFORE any execution intent is staged: a claim whose capture has no
/// resolvable provider transaction is refused without touching the claim, the
/// ledger or the budget, so no unexecutable intent is ever persisted.
/// </para>
/// </summary>
public enum SubscriptionRefundGatewayPreparationStatus
{
    /// <summary>The capture has a resolvable provider transaction; refunds may proceed.</summary>
    Ready = 1,

    /// <summary>
    /// The provider transaction could not be resolved (unknown attempt, a
    /// not-succeeded attempt, or a succeeded settlement with no stored
    /// transaction id). Fail-closed.
    /// </summary>
    MissingChargeTransaction = 2
}

public sealed record SubscriptionRefundGatewayPreparation(
    SubscriptionRefundGatewayPreparationStatus Status,
    string? ChargeTransactionId)
{
    public static SubscriptionRefundGatewayPreparation WithChargeTransaction(string chargeTransactionId) =>
        new(SubscriptionRefundGatewayPreparationStatus.Ready, chargeTransactionId);

    public static SubscriptionRefundGatewayPreparation Missing() =>
        new(SubscriptionRefundGatewayPreparationStatus.MissingChargeTransaction, null);
}

/// <summary>
/// The outcome of one refund call. The mapping is the money contract:
/// <see cref="SubscriptionRefundGatewayOutcome.Refunded"/> proves movement and
/// may only be produced with the provider's own refund reference;
/// <see cref="SubscriptionRefundGatewayOutcome.OutcomeUnknown"/> means the money
/// may or may not have moved and the case becomes reconciliation-only;
/// <see cref="SubscriptionRefundGatewayOutcome.Rejected"/> is a definitive
/// refusal that provably moved nothing.
/// </summary>
public enum SubscriptionRefundGatewayOutcome
{
    /// <summary>The provider accepted and returned a refund transaction reference.</summary>
    Refunded = 1,

    /// <summary>The provider definitively refused; no money moved.</summary>
    Rejected = 2,

    /// <summary>The outcome could not be confirmed; the money may or may not have moved.</summary>
    OutcomeUnknown = 3,

    /// <summary>The gateway is not configured; nothing was sent.</summary>
    NotConfigured = 4
}

public sealed record SubscriptionRefundGatewayResult(
    SubscriptionRefundGatewayOutcome Outcome,
    string? ProviderRefundId,
    /// <summary>The stable downstream error code (e.g. <c>Paymob.RefundRejected</c>) for audit; informational.</summary>
    string? ErrorCode)
{
    public static SubscriptionRefundGatewayResult UnknownBecauseReferenceMissing(string? errorCode) =>
        new(SubscriptionRefundGatewayOutcome.OutcomeUnknown, null, errorCode ?? "Paymob.RefundOutcomeUnknown");
}

public interface ISubscriptionRefundGateway
{
    /// <summary>
    /// Resolves the provider charge transaction of a succeeded capture. Called
    /// before any execution state is staged so an unexecutable claim fails
    /// closed without persisted intent.
    /// </summary>
    Task<SubscriptionRefundGatewayPreparation> PrepareAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Refunds <paramref name="awardMinorUnits"/> (piastres) of the named charge.
    /// Implementations must map a success whose refund reference is missing or
    /// blank to <see cref="SubscriptionRefundGatewayOutcome.OutcomeUnknown"/> —
    /// a 2xx from the provider with no reference cannot prove movement, and
    /// proving movement is the precondition for ever booking a completion.
    /// </summary>
    Task<SubscriptionRefundGatewayResult> RefundAsync(
        string chargeTransactionId,
        string currency,
        long awardMinorUnits,
        CancellationToken cancellationToken);
}
