using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Abstractions.Payments;

public sealed record PaymobBillingData(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber);

public sealed record PaymobPaymentIntentInput(
    BookingId BookingId,
    PaymentMethod Method,
    string? WalletNumber,
    decimal Amount,
    string Currency,
    PaymobBillingData Billing);

public sealed record PaymobPaymentIntent(
    string PaymobOrderId,
    string? IframeUrl,
    string? WalletRedirectUrl);

public interface IPaymobClient
{
    Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
        PaymobPaymentIntentInput input,
        CancellationToken cancellationToken = default);

    Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default);
}