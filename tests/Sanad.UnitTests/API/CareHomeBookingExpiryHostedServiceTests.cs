using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sanad.API.CareHomesIntegration;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.UnitTests.API;

public sealed class CareHomeBookingExpiryHostedServiceTests
{
    [Fact]
    public async Task Hosted_service_uses_configured_sweep_interval_and_expires_an_expired_hold()
    {
        await using var db = new CareHomesDbContext(
            new DbContextOptionsBuilder<CareHomesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var booking = CareHomeBooking.Create(
            CareHomeId.New(), UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), 100m, 0m, 0m, 1, "ع", "Elderly", 75, null,
            "Contact", "+201000000000", null, null, DateTime.UtcNow.AddMinutes(-16));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await using ServiceProvider provider = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IPaymobClient, NoopPaymob>()
            .BuildServiceProvider();
        using var service = new CareHomeBookingExpiryHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CareHomeBookingExpiryHostedService>.Instance,
            new CareHomeBookingTiming(
                TimeSpan.FromMinutes(15),
                TimeSpan.FromHours(24),
                TimeSpan.FromMilliseconds(10)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.StartAsync(cancellation.Token);
        while (booking.Status != CareHomeBookingStatus.Expired && !cancellation.IsCancellationRequested)
            await Task.Delay(10);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(CareHomeBookingStatus.Expired, booking.Status);
    }

    private sealed class NoopPaymob : IPaymobClient
    {
        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PaymobPaymentIntent>.Failure(
                new Error("Test.NotUsed", "Payment intent is not used by this sweep test.")));

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<string?>.Success(null));
    }
}
