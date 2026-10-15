using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sanad.GeographyImport;
using ImportProgram = Sanad.GeographyImport.Program;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class GeographyImportTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private const string Target = "synthetic-target-fingerprint";
    private static ImportCatalogue Mini() => ImportCatalogue.Create("synthetic-catalogue-hash",
    [
        new(LocationLevel.Governorate, "G", null, "محافظة", "Governorate"),
        new(LocationLevel.City, "C", "G", "مدينة", "City"),
        new(LocationLevel.Area, "A", "C", "منطقة", "Area")
    ]);

    [Fact]
    public async Task Preview_IsReadOnly_ThenApplyAndRepeatPreserveIds()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = fixture.Store;
        Assert.Empty(await store.ReadSnapshotAsync()); // prerequisite readback before scenario
        var plan = await store.PreviewAsync(Mini());
        Assert.False(plan.HasConflicts);
        Assert.Equal(3, plan.InsertCount);
        Assert.Empty(await store.ReadSnapshotAsync());
        plan = JsonSerializer.Deserialize<ImportPlan>(ImportJson.Bytes(plan), ImportJson.Options)!;
        Assert.Equal(3, await store.ApplyAsync(Mini(), plan, Target, "synthetic-backup-reference", "maintenance"));
        var snapshot = await store.ReadSnapshotAsync();
        Assert.Equal(plan.Rows.Select(row => row.Id).Order(), snapshot.Select(row => row.Id).Order());
        var repeated = await store.PreviewAsync(Mini());
        Assert.Equal(0, repeated.InsertCount);
        Assert.All(repeated.Rows, row => Assert.Equal(ImportAction.Keep, row.Action));
        Assert.Equal(0, await store.ApplyAsync(Mini(), repeated, Target, "synthetic-backup-reference", "maintenance"));
        Assert.Equal(ImportPlanner.Fingerprint(snapshot), ImportPlanner.Fingerprint(await store.ReadSnapshotAsync()));
    }

    [Fact]
    public async Task ManualRowsAndInactiveLeaf_PreserveIdsNamesStateAndTimestamps()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Store.PreviewAsync(Mini());
        await fixture.Store.ApplyAsync(Mini(), first, Target, "synthetic-backup", "maintenance");
        var area = await fixture.Db.Areas.SingleAsync();
        area.IsActive = false;
        var manual = new CityEntity
        {
            Id = Guid.NewGuid(), GovernorateId = (await fixture.Db.Governorates.SingleAsync()).Id,
            ArabicName = "سموحة", EnglishName = "Smouha", IsActive = false,
            CreatedOnUtc = Now.AddDays(-1), UpdatedOnUtc = Now.AddHours(-1)
        };
        fixture.Db.Cities.Add(manual);
        await fixture.Db.SaveChangesAsync();
        var before = await fixture.Store.ReadSnapshotAsync();
        Assert.Equal(4, before.Count);
        var plan = await fixture.Store.PreviewAsync(Mini());
        Assert.Contains(plan.Rows, row => row.SourceKey == "A" && row.Action == ImportAction.KeepInactive);
        Assert.Equal(manual.Id, Assert.Single(plan.PreservedExtras).Id);
        await fixture.Store.ApplyAsync(Mini(), plan, Target, "synthetic-backup", "maintenance");
        Assert.Equal(ImportPlanner.Fingerprint(before), ImportPlanner.Fingerprint(await fixture.Store.ReadSnapshotAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialOrAmbiguousNameMatches_FailClosed(bool ambiguous)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Governorates.Add(new GovernorateEntity
        {
            Id = Guid.NewGuid(), ArabicName = "محافظة", EnglishName = "Different", IsActive = true,
            CreatedOnUtc = Now, UpdatedOnUtc = Now
        });
        if (ambiguous) fixture.Db.Governorates.Add(new GovernorateEntity
        {
            Id = Guid.NewGuid(), ArabicName = "مختلفة", EnglishName = "Governorate", IsActive = true,
            CreatedOnUtc = Now, UpdatedOnUtc = Now
        });
        await fixture.Db.SaveChangesAsync();
        var before = await fixture.Store.ReadSnapshotAsync();
        var plan = await fixture.Store.PreviewAsync(Mini());
        Assert.Equal(ambiguous ? "AmbiguousNames" : "PartialNameMatch", plan.Rows[0].ConflictCode);
        Assert.True(plan.HasConflicts);
        await Assert.ThrowsAsync<ImportRejectedException>(() => fixture.Store.ApplyAsync(Mini(), plan, Target, "backup", "maintenance"));
        Assert.Equal(ImportPlanner.Fingerprint(before), ImportPlanner.Fingerprint(await fixture.Store.ReadSnapshotAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewRecordsUnderInactiveParentOrAncestor_AreNotActivated(bool activeCityUnderInactiveGovernorate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var governorate = new GovernorateEntity
        {
            Id = Guid.NewGuid(), ArabicName = "محافظة", EnglishName = "Governorate", IsActive = false,
            CreatedOnUtc = Now, UpdatedOnUtc = Now
        };
        fixture.Db.Add(governorate);
        if (activeCityUnderInactiveGovernorate) fixture.Db.Add(new CityEntity
        {
            Id = Guid.NewGuid(), GovernorateId = governorate.Id, ArabicName = "مدينة", EnglishName = "City",
            IsActive = true, CreatedOnUtc = Now, UpdatedOnUtc = Now
        });
        await fixture.Db.SaveChangesAsync();
        Assert.False((await fixture.Store.ReadSnapshotAsync())[0].IsActive);
        var plan = await fixture.Store.PreviewAsync(Mini());
        Assert.True(plan.HasConflicts);
        Assert.Contains(plan.Rows, row => row.ConflictCode == (activeCityUnderInactiveGovernorate ? "InactiveAncestor" : "InactiveParent"));
        Assert.False((await fixture.Db.Governorates.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task StalePreview_RejectsBeforeInsertingAnything()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Store.PreviewAsync(Mini());
        fixture.Db.Governorates.Add(new GovernorateEntity
        {
            Id = Guid.NewGuid(), ArabicName = "يدوية", EnglishName = "Manual", IsActive = true,
            CreatedOnUtc = Now, UpdatedOnUtc = Now
        });
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ImportRejectedException>(() => fixture.Store.ApplyAsync(Mini(), plan, Target, "backup", "maintenance"));
        Assert.Equal("StalePreview", error.Message);
        Assert.Single(await fixture.Store.ReadSnapshotAsync());
    }

    [Fact]
    public async Task TamperedPlan_IsRecomputedFromCatalogueBeforeWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Store.PreviewAsync(Mini());
        var alteredRows = plan.Rows.ToArray();
        alteredRows[0] = alteredRows[0] with { EnglishName = "Injected" };
        var error = await Assert.ThrowsAsync<ImportRejectedException>(() => fixture.Store.ApplyAsync(
            Mini(), plan with { Rows = alteredRows }, Target, "backup", "maintenance"));
        Assert.Equal("PlanTampered", error.Message);
        Assert.Empty(await fixture.Store.ReadSnapshotAsync());
    }

    [Theory]
    [InlineData("target")]
    [InlineData("catalogue")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("backup")]
    [InlineData("maintenance")]
    public async Task WrongAuthorityOrExpiredPlans_AreRefused(string fault)
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Store.PreviewAsync(Mini());
        plan = fault switch
        {
            "catalogue" => plan with { CatalogueSha256 = "wrong" },
            "expired" => plan with { CreatedOnUtc = Now.AddHours(-25) },
            "future" => plan with { CreatedOnUtc = Now.AddMinutes(1) },
            _ => plan
        };
        await Assert.ThrowsAsync<ImportRejectedException>(() => fixture.Store.ApplyAsync(Mini(), plan,
            fault == "target" ? "wrong" : Target, fault == "backup" ? "" : "backup", fault == "maintenance" ? "" : "maintenance"));
        Assert.Empty(await fixture.Store.ReadSnapshotAsync());
    }

    [Fact]
    public async Task FailureAfterFirstLevelSave_RollsBackWholeTransaction()
    {
        var failure = new FailSecondSave();
        await using var fixture = await Fixture.CreateAsync(failure);
        Assert.Empty(await fixture.Store.ReadSnapshotAsync());
        var plan = await fixture.Store.PreviewAsync(Mini());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ApplyAsync(Mini(), plan, Target, "backup", "maintenance"));
        Assert.Equal(2, failure.Attempts);
        Assert.Empty(await fixture.Store.ReadSnapshotAsync());
    }

    [Fact]
    public async Task OrphanTargetRows_AreReportedWithoutRepair()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Cities.Add(new CityEntity
        {
            Id = Guid.NewGuid(), GovernorateId = Guid.NewGuid(), ArabicName = "مدينة", EnglishName = "City", IsActive = true,
            CreatedOnUtc = Now, UpdatedOnUtc = Now
        });
        await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ImportRejectedException>(() => fixture.Store.PreviewAsync(Mini()));
        Assert.Equal("TargetOrphan", error.Message);
        Assert.Single(await fixture.Store.ReadSnapshotAsync());
    }

    [Fact]
    public void CatalogueValidation_RejectsBrokenNamesParentsKeysAndDuplicates()
    {
        var rows = Mini().Rows.ToArray();
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Create("hash", [rows[0], rows[0]]));
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Create("hash", [rows[0], rows[1] with { ParentKey = "missing" }]));
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Create("hash", [rows[0] with { EnglishName = "عربي" }]));
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Create("hash", [rows[0] with { ArabicName = " " }]));
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Create("hash", [rows[0] with { EnglishName = new string('x', 151) }]));
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Create("hash", [rows[0], rows[0] with { Key = "other" }]));
        Assert.Throws<ImportRejectedException>(() => ImportPlanner.Build(Mini(), [], Target, Now, _ => Guid.Empty));
        var id = Guid.NewGuid();
        Assert.Throws<ImportRejectedException>(() => ImportPlanner.Build(Mini(), [], Target, Now, _ => id));
    }

    [Fact]
    public void InputHashTypesAndArguments_AreStrict()
    {
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Parse(Encoding.UTF8.GetBytes("{}"), "wrong"));
        Assert.Throws<ImportRejectedException>(() => ImportProgram.ParseArguments(["preview", "--catalogue", "a", "--catalogue", "b"]));
        Assert.Throws<ImportRejectedException>(() => ImportProgram.ParseArguments(["apply", "--connection", "secret"]));
        Assert.Throws<ImportRejectedException>(() => ImportProgram.ParseArguments(["preview", "--catalogue", "a", "--report", "b", "--force", "true"]));
        var valid = ImportProgram.ParseArguments(["preview", "--catalogue", "a", "--report", "b"]);
        Assert.Equal("preview", valid.Mode);
        var bytes = Encoding.UTF8.GetBytes("{\"governorates\":[],\"cities\":[],\"areas\":[]}");
        Assert.Throws<ImportRejectedException>(() => ImportCatalogue.Parse(bytes, ImportJson.Hash(bytes)));
    }

    [Fact]
    public void AcceptedCatalogue_InitialAndRepeatPlansCoverAll6086Records()
    {
        var path = FindCatalogue();
        var bytes = File.ReadAllBytes(path);
        var catalogue = ImportCatalogue.Parse(bytes, ImportCatalogue.AcceptedV1Sha256);
        var first = ImportPlanner.Build(catalogue, [], Target, Now.AddTicks(3));
        Assert.Equal(6086, first.InsertCount);
        Assert.False(first.HasConflicts);
        Assert.Equal(0, first.CreatedOnUtc.Ticks % 10);
        var inserted = first.Rows.Select(row => new ExistingLocation(row.Level, row.Id, row.ParentId, row.ArabicName,
            row.EnglishName, true, first.CreatedOnUtc, first.CreatedOnUtc)).ToArray();
        var repeated = ImportPlanner.Build(catalogue, inserted, Target, Now);
        Assert.Equal(0, repeated.InsertCount);
        Assert.Empty(repeated.PreservedExtras);
        Assert.All(repeated.Rows, row => Assert.Equal(ImportAction.Keep, row.Action));
        Assert.Equal(first.Rows.Select(row => row.Id), repeated.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task EntireAcceptedCatalogue_IsTransactionalAndIdempotentOnMemorySqlite()
    {
        var catalogue = ImportCatalogue.Parse(File.ReadAllBytes(FindCatalogue()), ImportCatalogue.AcceptedV1Sha256);
        await using var fixture = await Fixture.CreateAsync();
        Assert.Empty(await fixture.Store.ReadSnapshotAsync());
        var preview = await fixture.Store.PreviewAsync(catalogue);
        Assert.Equal(6086, preview.InsertCount);
        Assert.Equal(6086, await fixture.Store.ApplyAsync(catalogue, preview, Target, "synthetic-backup", "synthetic-maintenance"));
        var before = await fixture.Store.ReadSnapshotAsync();
        var repeat = await fixture.Store.PreviewAsync(catalogue);
        Assert.Equal(0, repeat.InsertCount);
        Assert.Equal(0, await fixture.Store.ApplyAsync(catalogue, repeat, Target, "synthetic-backup", "synthetic-maintenance"));
        Assert.Equal(ImportPlanner.Fingerprint(before), ImportPlanner.Fingerprint(await fixture.Store.ReadSnapshotAsync()));
    }

    [Fact]
    public async Task ExistingReportsAndWrongReviewedFileHash_AreRefusedBeforeReadingConnection()
    {
        var path = FindCatalogue();
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(ImportCatalogue.AcceptedV1Sha256, ImportJson.Hash(bytes));
        Assert.Equal(2, await ImportProgram.Main(["preview", "--catalogue", path, "--report", path]));
        Assert.Equal(2, await ImportProgram.Main(["apply", "--catalogue", path, "--plan", path,
            "--plan-sha", "wrong", "--confirm-target", "synthetic", "--backup-reference", "synthetic", "--maintenance-reference", "synthetic"]));
        Assert.Equal(ImportJson.Hash(bytes), ImportJson.Hash(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Cancellation_DoesNotWriteAnyRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plan = await fixture.Store.PreviewAsync(Mini());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.ApplyAsync(Mini(), plan, Target, "backup", "maintenance", cancelled.Token));
        Assert.Empty(await fixture.Store.ReadSnapshotAsync());
    }

    [Fact]
    public void NpgsqlMappingAndLockSql_UseOnlyTheThreeExistingTablesWithoutConnecting()
    {
        using var db = new LocationDbContext(new DbContextOptionsBuilder<LocationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=never_opened;Username=test;Password=test").Options);
        Assert.Equal(3, db.Model.GetEntityTypes().Count());
        Assert.Contains("caregivers.governorates", db.Governorates.ToQueryString());
        Assert.Contains("caregivers.cities", db.Cities.ToQueryString());
        Assert.Contains("caregivers.areas", db.Areas.ToQueryString());
        Assert.Equal("LOCK TABLE caregivers.governorates, caregivers.cities, caregivers.areas IN SHARE ROW EXCLUSIVE MODE", ImportStore.WriterLockSql);
    }

    private static string FindCatalogue()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "docs", "internal", "work", "portal-api-reconciliation", "reference-data", "egypt", "candidate.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Candidate required in repository checkout.");
    }

    private sealed class FailSecondSave : SaveChangesInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (++Attempts == 2) throw new InvalidOperationException("Synthetic failure after the governorate save.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture(SqliteConnection connection, LocationDbContext db) : IAsyncDisposable
    {
        public LocationDbContext Db => db;
        public ImportStore Store { get; } = new(db, Target, () => Now);
        public static async Task<Fixture> CreateAsync(SaveChangesInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<LocationDbContext>().UseSqlite(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var db = new LocationDbContext(options.Options);
            await db.Database.EnsureCreatedAsync(); // new per-test memory-only database, never EnsureDeleted
            return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
