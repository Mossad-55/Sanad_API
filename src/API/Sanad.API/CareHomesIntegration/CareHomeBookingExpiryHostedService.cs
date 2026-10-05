using Sanad.Modules.CareHomes.Infrastructure.Bookings;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Payments;

namespace Sanad.API.CareHomesIntegration;

public sealed class CareHomeBookingExpiryHostedService(IServiceScopeFactory scopes, ILogger<CareHomeBookingExpiryHostedService> logger, CareHomeBookingTiming timing) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(timing.ExpirySweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using IServiceScope scope = scopes.CreateScope(); await CareHomeBookingExpiryService.SweepAsync(scope.ServiceProvider.GetRequiredService<CareHomesDbContext>(), scope.ServiceProvider.GetRequiredService<IPaymobClient>(), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Care-home booking expiry sweep failed."); }
        }
    }
}
