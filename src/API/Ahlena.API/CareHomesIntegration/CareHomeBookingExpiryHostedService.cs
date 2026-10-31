using Ahlena.Modules.CareHomes.Infrastructure.Bookings;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.CareHomes.Infrastructure.Visits;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.Families.Application.Abstractions.Payments;

namespace Ahlena.API.CareHomesIntegration;

public sealed class CareHomeBookingExpiryHostedService(IServiceScopeFactory scopes, ILogger<CareHomeBookingExpiryHostedService> logger, CareHomeBookingTiming timing) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(timing.ExpirySweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                CareHomesDbContext db = scope.ServiceProvider.GetRequiredService<CareHomesDbContext>();
                await CareHomeBookingExpiryService.SweepAsync(db, scope.ServiceProvider.GetRequiredService<IPaymobClient>(), stoppingToken);
                await CareHomeVisitExpiryService.SweepAsync(db, DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Care-home booking expiry sweep failed."); }
        }
    }
}
