using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.Modules.Families.Infrastructure.Payments;

public sealed class DevelopmentPaymobClient : IPaymobClient
{
    public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
        PaymobPaymentIntentInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<PaymobPaymentIntent>.Success(
            new PaymobPaymentIntent(
                input.BookingId.Value.ToString(),
                $"dev-intention-{Guid.NewGuid():N}",
                $"dev-secret-{Guid.NewGuid():N}",
                "pk_dev")));
    }

    public Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<string?>.Success($"dev-refund-{Guid.NewGuid():N}"));
    }
}