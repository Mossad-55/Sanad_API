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
                $"dev-{Guid.NewGuid():N}",
                null,
                null)));
    }
}