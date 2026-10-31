using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Ahlena.GeographyImport;

public sealed class ImportStore(LocationDbContext db, string targetFingerprint, Func<DateTime>? clock = null,
    bool useApprovedVpsLinks = false)
{
    public const string WriterLockSql = "LOCK TABLE caregivers.governorates, caregivers.cities, caregivers.areas IN SHARE ROW EXCLUSIVE MODE";
    private DateTime Now => (clock ?? (() => DateTime.UtcNow))();

    public async Task<IReadOnlyList<ExistingLocation>> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<ExistingLocation>();
        void Add(LocationLevel level, LookupEntity row, Guid? parent) => rows.Add(new(level, row.Id, parent,
            row.ArabicName, row.EnglishName, row.IsActive, DateTime.SpecifyKind(row.CreatedOnUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(row.UpdatedOnUtc, DateTimeKind.Utc)));
        foreach (var row in await db.Governorates.AsNoTracking().ToListAsync(cancellationToken)) Add(LocationLevel.Governorate, row, null);
        foreach (var row in await db.Cities.AsNoTracking().ToListAsync(cancellationToken)) Add(LocationLevel.City, row, row.GovernorateId);
        foreach (var row in await db.Areas.AsNoTracking().ToListAsync(cancellationToken)) Add(LocationLevel.Area, row, row.CityId);
        if (rows.Count > 100_000) throw new ImportRejectedException("TargetTooLarge");
        return rows;
    }

    public async Task<ImportPlan> PreviewAsync(ImportCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        return ImportPlanner.Build(catalogue, await ReadSnapshotAsync(cancellationToken), targetFingerprint, Now,
            useApprovedVpsLinks: useApprovedVpsLinks);
        // Disposal rolls back the read-only transaction; no database write occurs.
    }

    public async Task<int> ApplyAsync(ImportCatalogue catalogue, ImportPlan reviewedPlan,
        string confirmedTarget, string backupReference, string maintenanceReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupReference) || backupReference.Length > 200)
            throw new ImportRejectedException("BackupReferenceRequired");
        if (string.IsNullOrWhiteSpace(maintenanceReference) || maintenanceReference.Length > 200)
            throw new ImportRejectedException("MaintenanceReferenceRequired");
        if (reviewedPlan.Reconciliation != (useApprovedVpsLinks ? ApprovedVpsLinks.Profile : null) ||
            reviewedPlan.Version != 1 || reviewedPlan.CatalogueSha256 != catalogue.Sha256 ||
            reviewedPlan.TargetFingerprint != targetFingerprint || confirmedTarget != targetFingerprint)
            throw new ImportRejectedException("PlanTargetOrCatalogue");
        if (reviewedPlan.CreatedOnUtc.Kind != DateTimeKind.Utc || reviewedPlan.CreatedOnUtc > Now ||
            Now - reviewedPlan.CreatedOnUtc > TimeSpan.FromHours(24)) throw new ImportRejectedException("PlanExpiredOrFuture");
        if (reviewedPlan.HasConflicts) throw new ImportRejectedException("PlanHasConflicts");
        if (db.ChangeTracker.HasChanges()) throw new ImportRejectedException("PendingContextChanges");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", cancellationToken);
                await db.Database.ExecuteSqlRawAsync(WriterLockSql, cancellationToken);
            }
            var snapshot = await ReadSnapshotAsync(cancellationToken);
            if (ImportPlanner.Fingerprint(snapshot) != reviewedPlan.BaselineFingerprint)
                throw new ImportRejectedException("StalePreview");
            var proposedIds = reviewedPlan.Rows.Where(row => row.Action == ImportAction.Insert)
                .ToDictionary(row => row.SourceKey, row => row.Id, StringComparer.Ordinal);
            var recomputed = ImportPlanner.Build(catalogue, snapshot, targetFingerprint, reviewedPlan.CreatedOnUtc,
                row => proposedIds.TryGetValue(row.Key, out var id) ? id : throw new ImportRejectedException("MissingReviewedId"),
                useApprovedVpsLinks);
            if (!ImportJson.Bytes(recomputed).SequenceEqual(ImportJson.Bytes(reviewedPlan)))
                throw new ImportRejectedException("PlanTampered");

            foreach (var group in reviewedPlan.Rows.Where(row => row.Action == ImportAction.Insert).GroupBy(row => row.Level))
            {
                foreach (var row in group)
                {
                    LookupEntity entity = row.Level switch
                    {
                        LocationLevel.Governorate => new GovernorateEntity(),
                        LocationLevel.City => new CityEntity { GovernorateId = row.ParentId!.Value },
                        LocationLevel.Area => new AreaEntity { CityId = row.ParentId!.Value },
                        _ => throw new ImportRejectedException("InvalidLevel")
                    };
                    entity.Id = row.Id;
                    entity.ArabicName = row.ArabicName;
                    entity.EnglishName = row.EnglishName;
                    entity.IsActive = true;
                    entity.CreatedOnUtc = entity.UpdatedOnUtc = reviewedPlan.CreatedOnUtc;
                    db.Add(entity);
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            var after = await ReadSnapshotAsync(cancellationToken);
            var expected = snapshot.Concat(reviewedPlan.Rows.Where(row => row.Action == ImportAction.Insert)
                .Select(row => new ExistingLocation(row.Level, row.Id, row.ParentId, row.ArabicName, row.EnglishName,
                    true, reviewedPlan.CreatedOnUtc, reviewedPlan.CreatedOnUtc))).ToArray();
            if (ImportPlanner.Fingerprint(after) != ImportPlanner.Fingerprint(expected))
                throw new ImportRejectedException("PostInsertVerification");
            await transaction.CommitAsync(cancellationToken);
            return reviewedPlan.InsertCount;
        }
        finally { db.ChangeTracker.Clear(); }
        // Exception/cancellation disposes uncommitted transaction: no partial import.
    }
}
