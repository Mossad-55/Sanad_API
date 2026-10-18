using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.API.FamiliesIntegration;

public sealed class CaregiverMedicalAccessAuditRetentionService(
    IServiceScopeFactory scopeFactory,
    ILogger<CaregiverMedicalAccessAuditRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Caregiver medical access audit retention cleanup failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task CleanupOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IFamiliesDbContext>();
        var cutoff = DateTime.UtcNow.AddMonths(-12);

        while (!cancellationToken.IsCancellationRequested)
        {
            var ids = await db.CaregiverMedicalAccessAudits
                .AsNoTracking()
                .Where(x => x.OccurredOnUtc < cutoff)
                .OrderBy(x => x.OccurredOnUtc)
                .Take(BatchSize)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0) break;

            await db.CaregiverMedicalAccessAudits
                .Where(x => ids.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);

            if (ids.Count < BatchSize) break;
        }
    }
}