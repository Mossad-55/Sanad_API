using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Subscriptions;

namespace Ahlena.Modules.Families.Application.Abstractions.Payments;

public sealed record PaymobBillingData(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber);

public sealed record PaymobPaymentIntentInput(
    BookingId BookingId,
    PaymentMethod Method,
    decimal Amount,
    string Currency,
    PaymobBillingData Billing,
    string? MerchantReference = null);

public sealed record PaymobSubscriptionPaymentIntentInput(
    string MerchantReference,
    SubscriptionPaymentMethod Method,
    decimal Amount,
    string Currency,
    PaymobBillingData Billing,
    int? SubscriptionPlanId = null);

public sealed record PaymobPaymentIntent(
    string PaymobOrderId,
    string IntentionOrderId,
    string ClientSecret,
    string PublicKey,
    bool RecurringRenewalSupported = false);

/// <summary>
/// The authenticated provider view of a single transaction, read back from
/// Paymob's transaction-status API over the merchant channel. Every field here
/// is provider-attested (not a webhook body), so it — unlike a callback's
/// unsigned <c>is_refund</c> — may evidence a settled refund. Missing scalar
/// fields are surfaced as <c>null</c> so a caller can refuse to act on an
/// incomplete readback rather than infer a value.
/// </summary>
public sealed record PaymobRefundVerification(
    long? TransactionId,
    bool? IsRefund,
    bool? Success,
    bool? Pending,
    long? AmountCents,
    string? Currency,
    long? OrderId,
    long? ParentTransactionId);

public interface IPaymobClient
{
    Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
        PaymobPaymentIntentInput input,
        CancellationToken cancellationToken = default);

    Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
        PaymobSubscriptionPaymentIntentInput input,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result<PaymobPaymentIntent>.Failure(
            new Error("Paymob.MethodNotAvailable", "Subscription payments are not available for this client.")));

    Task<Result> UpdateSubscriptionAmountAsync(
        string providerSubscriptionId,
        decimal targetRecurringGross,
        CancellationToken cancellationToken = default) => Task.FromResult(Result.Failure(
            new Error("Paymob.MethodNotAvailable", "Subscription updates are not available for this client.")));

    Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single transaction back from the provider, authenticated, to
    /// verify a refund that the refund API's <c>id</c>-only response does not by
    /// itself prove settled. Returns a failure (never a fabricated success) when
    /// the gateway is unconfigured, the read is refused, the response cannot be
    /// read, or the required fields are absent — callers must treat any failure
    /// as "not verified" and leave the refund at its current state.
    /// </summary>
    Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
        string refundTransactionId,
        CancellationToken cancellationToken = default);
}
