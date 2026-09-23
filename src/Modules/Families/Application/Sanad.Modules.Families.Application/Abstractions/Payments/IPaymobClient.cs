using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Abstractions.Payments;

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
    PaymobBillingData Billing);

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

    Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default);
}
