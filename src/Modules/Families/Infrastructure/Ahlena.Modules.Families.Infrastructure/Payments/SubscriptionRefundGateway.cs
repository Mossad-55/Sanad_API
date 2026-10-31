using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Families.Infrastructure.Payments;

/// <summary>
/// The Families implementation of the Finance duplicate-claim refund gateway
/// seam. The purchase owner resolves the provider charge transaction from its
/// own payment history and delegates the refund call to the same
/// <see cref="IPaymobClient"/> every booking refund path uses, so one provider
/// client and one outcome mapping govern all refunds.
/// <para>
/// The outcome mapping is the money contract (Owner-accepted design): a 2xx
/// with a usable refund reference is
/// <see cref="SubscriptionRefundGatewayOutcome.Refunded"/>; a definitive
/// provider refusal is
/// <see cref="SubscriptionRefundGatewayOutcome.Rejected"/>; EVERYTHING else —
/// an unconfirmable outcome, a success without a usable reference, an
/// unexpected gateway error — is
/// <see cref="SubscriptionRefundGatewayOutcome.OutcomeUnknown"/>, because the
/// gateway cannot prove the absence of money movement and only the provider
/// can. Reconciliation, never retry, untangles an unknown.
/// </para>
/// </summary>
public sealed class SubscriptionRefundGateway(IFamiliesDbContext familiesDb, IPaymobClient paymob)
    : ISubscriptionRefundGateway
{
    /// <summary>The provider refund is in EGP with two decimals; piastres convert exactly.</summary>
    private const int MinorUnitsPerMajorUnit = 100;

    public async Task<SubscriptionRefundGatewayPreparation> PrepareAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        if (paymentAttemptId == Guid.Empty)
            return SubscriptionRefundGatewayPreparation.Missing();

        SubscriptionPaymentAttempt? captured = await familiesDb
            .SubscriptionPaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == paymentAttemptId &&
                             candidate.Status == SubscriptionPaymentAttemptStatus.Succeeded &&
                             candidate.PaymobTransactionId != null,
                cancellationToken);

        return captured?.PaymobTransactionId is { } chargeTransactionId
            ? SubscriptionRefundGatewayPreparation.WithChargeTransaction(chargeTransactionId)
            : SubscriptionRefundGatewayPreparation.Missing();
    }

    public async Task<SubscriptionRefundGatewayResult> RefundAsync(
        string chargeTransactionId,
        string currency,
        long awardMinorUnits,
        CancellationToken cancellationToken)
    {
        // The claim award is a verified EGP amount by construction (the money
        // core fixes the currency); anything else is a precondition violation
        // and must not be sent to the provider as-is.
        if (!string.Equals(currency, Money.CurrencyCode, StringComparison.Ordinal))
        {
            return new SubscriptionRefundGatewayResult(
                SubscriptionRefundGatewayOutcome.Rejected,
                null,
                "Paymob.CurrencyUnsupported");
        }

        if (string.IsNullOrWhiteSpace(chargeTransactionId) || awardMinorUnits <= 0)
        {
            return new SubscriptionRefundGatewayResult(
                SubscriptionRefundGatewayOutcome.Rejected,
                null,
                "Paymob.InvalidRefundRequest");
        }

        Result<string?> refund = await paymob.RefundPaymentAsync(
            chargeTransactionId,
            awardMinorUnits / (decimal)MinorUnitsPerMajorUnit,
            cancellationToken);

        if (refund.IsSuccess)
        {
            return string.IsNullOrWhiteSpace(refund.Value)
                ? SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing("Paymob.RefundOutcomeUnknown")
                : new SubscriptionRefundGatewayResult(
                    SubscriptionRefundGatewayOutcome.Refunded,
                    refund.Value,
                    null);
        }

        return refund.Error?.Code switch
        {
            // The provider definitively refused: provably nothing moved.
            "Paymob.RefundRejected" => new SubscriptionRefundGatewayResult(
                SubscriptionRefundGatewayOutcome.Rejected,
                null,
                "Paymob.RefundRejected"),
            // Nothing was ever sent.
            "Paymob.NotConfigured" => new SubscriptionRefundGatewayResult(
                SubscriptionRefundGatewayOutcome.NotConfigured,
                null,
                "Paymob.NotConfigured"),
            // An unexpected gateway error cannot prove the absence of movement.
            _ => SubscriptionRefundGatewayResult.UnknownBecauseReferenceMissing(refund.Error?.Code)
        };
    }
}
