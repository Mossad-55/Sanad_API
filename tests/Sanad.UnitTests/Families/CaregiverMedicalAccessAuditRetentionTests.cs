using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sanad.API.FamiliesIntegration;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.UnitTests.Support;

namespace Sanad.UnitTests.Families;

public sealed class CaregiverMedicalAccessAuditRetentionTests
{
    [Fact]
    public async Task CleanupOnceAsync_RemovesOnlyExpiredRows()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        await using var db = new FamiliesDbContext(options);
        await db.Database.EnsureCreatedAsync();

        DateTime cutoff = DateTime.UtcNow.AddMonths(-12);
        List<CaregiverMedicalAccessAudit> expired = Enumerable.Range(0, 501)
            .Select(_ => CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
                CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
                null, null, null, null, null, cutoff.AddMinutes(-1), Guid.NewGuid().ToString()))
            .ToList();
        var nearCutoff = CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, cutoff.AddMinutes(2), Guid.NewGuid().ToString());
        var fresh = CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString());
        db.CaregiverMedicalAccessAudits.AddRange(expired);
        db.CaregiverMedicalAccessAudits.AddRange(nearCutoff, fresh);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton<IFamiliesDbContext>(db);
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        // IServiceScopeFactory not registered by default; use a simple factory
        var factory = new SimpleScopeFactory(provider);
        var svc = new CaregiverMedicalAccessAuditRetentionService(factory, NullLogger<CaregiverMedicalAccessAuditRetentionService>.Instance);
        await svc.CleanupOnceAsync(CancellationToken.None);

        Assert.Equal(2, await db.CaregiverMedicalAccessAudits.CountAsync());
        Assert.Contains(await db.CaregiverMedicalAccessAudits.Select(x => x.Id).ToListAsync(), id => id == nearCutoff.Id);
        Assert.Contains(await db.CaregiverMedicalAccessAudits.Select(x => x.Id).ToListAsync(), id => id == fresh.Id);
    }

    [Fact]
    public void TrackedDelete_IsBlocked()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        using var db = new FamiliesDbContext(options);
        db.Database.EnsureCreated();
        var row = CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString());
        db.CaregiverMedicalAccessAudits.Add(row);
        db.SaveChanges();
        row = db.CaregiverMedicalAccessAudits.Single();
        db.CaregiverMedicalAccessAudits.Remove(row);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void TrackedMutation_IsBlocked()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        using var db = new FamiliesDbContext(options);
        db.Database.EnsureCreated();
        var row = CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString());
        db.CaregiverMedicalAccessAudits.Add(row);
        db.SaveChanges();
        row = db.CaregiverMedicalAccessAudits.Single();
        db.Entry(row).State = EntityState.Modified;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        row = db.CaregiverMedicalAccessAudits.Single();
        db.CaregiverMedicalAccessAudits.Remove(row);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    private sealed class SimpleScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new SimpleScope(provider);
    }

    private sealed class SimpleScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;
        public void Dispose() { }
    }
}
