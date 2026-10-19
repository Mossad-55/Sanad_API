using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// E04-A control plane: domain guards, persistence store behavior
/// (optimistic version conflict, append-only attributable audit, fail-closed
/// reads), the query/command handlers and the Finance registration. No live
/// database, no provider call and no applied migration is involved.
/// </summary>
public sealed class PlatformControlsTests
{
    private static readonly Guid SeedId =
        Guid.Parse("0198e2c2-4444-7777-8888-0000000000e4");

    private static readonly Guid ActorId =
        Guid.Parse("0198e2c2-4444-7777-8888-0000000000a1");

    [Fact]
    public void Create_validates_state_timestamp_version_and_id()
    {
        Assert.Throws<DomainException>(() => PlatformControlState.Create(
            SeedId, (PlatformState)99, null, null, true, true, 1, Utc(1)));
        Assert.Throws<DomainException>(() => PlatformControlState.Create(
            SeedId,
            PlatformState.Operational,
            null,
            null,
            true,
            true,
            1,
            new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Local)));
        Assert.Throws<DomainException>(() => PlatformControlState.Create(
            SeedId, PlatformState.Operational, null, null, true, true, 0, Utc(1)));
        Assert.Throws<DomainException>(() => PlatformControlState.Create(
            Guid.Empty, PlatformState.Operational, null, null, true, true, 1, Utc(1)));
        Assert.Throws<DomainException>(() => PlatformControlState.Create(
            SeedId,
            PlatformState.Operational,
            new string('x', PlatformControlState.MaximumMessageLength + 1),
            null,
            true,
            true,
            1,
            Utc(1)));
    }

    [Fact]
    public void Create_normalizes_blank_messages_and_preserves_values()
    {
        PlatformControlState state = PlatformControlState.Create(
            SeedId,
            PlatformState.Maintenance,
            "   ",
            "  Trimmed  ",
            cardEnabled: false,
            walletEnabled: true,
            version: 3,
            Utc(2));

        Assert.Equal(PlatformState.Maintenance, state.State);
        Assert.Null(state.MaintenanceMessageArabic);
        Assert.Equal("Trimmed", state.MaintenanceMessageEnglish);
        Assert.False(state.CardEnabled);
        Assert.True(state.WalletEnabled);
        Assert.Equal(3, state.Version);
        Assert.Equal(Utc(2), state.UpdatedOnUtc);
    }

    [Fact]
    public void Apply_validates_before_mutating_and_advances_the_version_once()
    {
        PlatformControlState state = SeededRow();

        Assert.Throws<DomainException>(() => state.Apply(
            (PlatformState)7, null, null, false, false, Utc(2)));

        Assert.Equal(PlatformState.Operational, state.State);
        Assert.Equal(1, state.Version);
        Assert.True(state.CardEnabled);

        state.Apply(PlatformState.Maintenance, "صيانة", "Maintenance window", false, true, Utc(2));

        Assert.Equal(PlatformState.Maintenance, state.State);
        Assert.Equal("صيانة", state.MaintenanceMessageArabic);
        Assert.False(state.CardEnabled);
        Assert.True(state.WalletEnabled);
        Assert.Equal(2, state.Version);
        Assert.Equal(Utc(2), state.UpdatedOnUtc);
    }

    [Fact]
    public void Audit_entry_requires_an_actor_and_contiguous_versions()
    {
        Assert.Throws<DomainException>(() => PlatformControlAuditEntry.Create(
            Guid.Empty, 1, 2, PlatformState.Operational, PlatformState.Maintenance,
            null, null, null, null, true, false, true, true, null, Utc(1)));
        Assert.Throws<DomainException>(() => PlatformControlAuditEntry.Create(
            ActorId, 1, 3, PlatformState.Operational, PlatformState.Maintenance,
            null, null, null, null, true, false, true, true, null, Utc(1)));
        Assert.Throws<DomainException>(() => PlatformControlAuditEntry.Create(
            ActorId, 0, 1, PlatformState.Operational, PlatformState.Maintenance,
            null, null, null, null, true, false, true, true, null, Utc(1)));
        Assert.Throws<DomainException>(() => PlatformControlAuditEntry.Create(
            ActorId, 1, 2, PlatformState.Operational, PlatformState.Maintenance,
            null, null, null, null, true, false, true, true, null,
            new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Local)));
        Assert.Throws<DomainException>(() => PlatformControlAuditEntry.Create(
            ActorId, 1, 2, PlatformState.Operational, PlatformState.Maintenance,
            null, null, null, null, true, false, true, true,
            new string('x', PlatformControlAuditEntry.MaximumReasonLength + 1), Utc(1)));
    }

    [Fact]
    public void Audit_entry_normalizes_and_bounds_the_captured_messages()
    {
        PlatformControlAuditEntry entry = PlatformControlAuditEntry.Create(
            ActorId,
            1,
            2,
            PlatformState.Operational,
            PlatformState.Maintenance,
            "  نص قبل  ",
            "   ",
            " Before ",
            null,
            true,
            false,
            true,
            true,
            "  internal reason  ",
            Utc(1));

        Assert.Equal("نص قبل", entry.BeforeMaintenanceMessageArabic);
        Assert.Null(entry.AfterMaintenanceMessageArabic);
        Assert.Equal("Before", entry.BeforeMaintenanceMessageEnglish);
        Assert.Null(entry.AfterMaintenanceMessageEnglish);
        Assert.Equal("internal reason", entry.InternalReason);
        Assert.Equal(1, entry.PreviousVersion);
        Assert.Equal(2, entry.NewVersion);

        Assert.Throws<DomainException>(() => PlatformControlAuditEntry.Create(
            ActorId, 1, 2, PlatformState.Operational, PlatformState.Maintenance,
            null,
            new string('x', PlatformControlState.MaximumMessageLength + 1),
            null, null, true, false, true, true, null, Utc(1)));
    }

    [Fact]
    public void Audit_entry_is_append_only_and_exposes_no_public_mutator()
    {
        Type type = typeof(PlatformControlAuditEntry);

        Assert.True(type.IsSealed);
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            property => Assert.Null(property.GetSetMethod(nonPublic: false)));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => method.Name),
            name => name is "Apply" or "Update" or "Set" or "Delete");
    }

    [Fact]
    public async Task Store_read_returns_null_and_update_fails_closed_without_a_record()
    {
        await using FinanceDbContext db = CreateContext();
        var store = new PlatformControlStore(db);

        Assert.Null(await store.GetAsync(default));

        Result<PlatformControlSnapshot> update = await store.UpdateAsync(
            1,
            new PlatformControlValues(PlatformState.Operational, null, null, true, true),
            ActorId,
            null,
            default);

        Assert.False(update.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", update.Error.Code);
        Assert.Empty(db.PlatformControlAuditEntries);
        Assert.Empty(db.PlatformControlStates);
    }

    [Fact]
    public async Task Store_update_persists_state_and_appends_attributable_before_after_audit()
    {
        await using FinanceDbContext db = CreateContext();
        db.PlatformControlStates.Add(SeededRow(messageArabic: "صيانة قديمة", messageEnglish: "Old message"));
        await db.SaveChangesAsync();
        var store = new PlatformControlStore(db);

        Result<PlatformControlSnapshot> result = await store.UpdateAsync(
            1,
            new PlatformControlValues(PlatformState.Maintenance, "صيانة", "Maintenance", false, true),
            ActorId,
            "  Scheduled gateway upgrade  ",
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal(PlatformState.Maintenance, result.Value.Values.State);
        Assert.False(result.Value.Values.CardEnabled);
        Assert.True(result.Value.Values.WalletEnabled);
        Assert.Equal("Maintenance", result.Value.Values.MaintenanceMessageEnglish);

        PlatformControlAuditEntry audit = await db.PlatformControlAuditEntries.SingleAsync();
        Assert.Equal(ActorId, audit.ActorUserId);
        Assert.Equal(1, audit.PreviousVersion);
        Assert.Equal(2, audit.NewVersion);
        Assert.Equal(PlatformState.Operational, audit.BeforeState);
        Assert.Equal(PlatformState.Maintenance, audit.AfterState);
        Assert.True(audit.BeforeCardEnabled);
        Assert.False(audit.AfterCardEnabled);
        Assert.True(audit.BeforeWalletEnabled);
        Assert.True(audit.AfterWalletEnabled);
        Assert.Equal("صيانة قديمة", audit.BeforeMaintenanceMessageArabic);
        Assert.Equal("صيانة", audit.AfterMaintenanceMessageArabic);
        Assert.Equal("Old message", audit.BeforeMaintenanceMessageEnglish);
        Assert.Equal("Maintenance", audit.AfterMaintenanceMessageEnglish);
        Assert.Equal("Scheduled gateway upgrade", audit.InternalReason);
        Assert.Equal(result.Value.UpdatedOnUtc, audit.OccurredOnUtc);

        Assert.Equal(PlatformState.Maintenance, (await store.GetAsync(default))!.Values.State);
    }

    [Fact]
    public async Task Store_update_records_message_clear_and_set_transitions_in_order()
    {
        await using FinanceDbContext db = CreateContext();
        db.PlatformControlStates.Add(SeededRow(messageArabic: "نص قديم", messageEnglish: "Old"));
        await db.SaveChangesAsync();
        var store = new PlatformControlStore(db);

        // First change clears the public message.
        Assert.True((await store.UpdateAsync(
            1,
            new PlatformControlValues(PlatformState.Maintenance, null, null, true, true),
            ActorId,
            "start maintenance",
            default)).IsSuccess);

        // Second change publishes a new message.
        Assert.True((await store.UpdateAsync(
            2,
            new PlatformControlValues(PlatformState.Operational, "نص جديد", "New", true, true),
            ActorId,
            "end maintenance",
            default)).IsSuccess);

        List<PlatformControlAuditEntry> history = await db.PlatformControlAuditEntries
            .OrderBy(entry => entry.NewVersion)
            .ToListAsync();

        Assert.Equal([2, 3], history.Select(entry => entry.NewVersion));

        PlatformControlAuditEntry cleared = history[0];
        Assert.Equal("نص قديم", cleared.BeforeMaintenanceMessageArabic);
        Assert.Null(cleared.AfterMaintenanceMessageArabic);
        Assert.Equal("Old", cleared.BeforeMaintenanceMessageEnglish);
        Assert.Null(cleared.AfterMaintenanceMessageEnglish);

        PlatformControlAuditEntry published = history[1];
        Assert.Null(published.BeforeMaintenanceMessageArabic);
        Assert.Equal("نص جديد", published.AfterMaintenanceMessageArabic);
        Assert.Null(published.BeforeMaintenanceMessageEnglish);
        Assert.Equal("New", published.AfterMaintenanceMessageEnglish);

        PlatformControlSnapshot reloaded = (await store.GetAsync(default))!;
        Assert.Equal(3, reloaded.Version);
        Assert.Equal("نص جديد", reloaded.Values.MaintenanceMessageArabic);
        Assert.Equal("New", reloaded.Values.MaintenanceMessageEnglish);
    }

    [Fact]
    public async Task Store_update_rejects_invalid_values_without_mutating_state_or_writing_audit()
    {
        await using FinanceDbContext db = CreateContext();
        db.PlatformControlStates.Add(SeededRow());
        await db.SaveChangesAsync();
        var store = new PlatformControlStore(db);

        Result<PlatformControlSnapshot> result = await store.UpdateAsync(
            1,
            new PlatformControlValues(
                PlatformState.Maintenance,
                new string('x', PlatformControlState.MaximumMessageLength + 1),
                null,
                false,
                false),
            ActorId,
            null,
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.Control.Invalid", result.Error.Code);

        PlatformControlState persisted = await db.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.Equal(PlatformState.Operational, persisted.State);
        Assert.Equal(1, persisted.Version);
        Assert.True(persisted.CardEnabled);
        Assert.Empty(db.PlatformControlAuditEntries);
    }

    [Fact]
    public async Task Store_update_rejects_a_stale_expected_version_and_preserves_newer_state()
    {
        await using FinanceDbContext db = CreateContext();
        db.PlatformControlStates.Add(SeededRow());
        await db.SaveChangesAsync();
        var store = new PlatformControlStore(db);

        Assert.True((await store.UpdateAsync(
            1,
            new PlatformControlValues(PlatformState.Maintenance, null, "Maintenance", false, false),
            ActorId,
            null,
            default)).IsSuccess);

        Result<PlatformControlSnapshot> stale = await store.UpdateAsync(
            1,
            new PlatformControlValues(PlatformState.Operational, null, null, true, true),
            ActorId,
            "stale retry",
            default);

        Assert.False(stale.IsSuccess);
        Assert.Equal("Platform.ControlVersionConflict", stale.Error.Code);

        PlatformControlState persisted = await db.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.Equal(PlatformState.Maintenance, persisted.State);
        Assert.Equal(2, persisted.Version);
        Assert.False(persisted.CardEnabled);
        Assert.Single(db.PlatformControlAuditEntries);
    }

    [Fact]
    public async Task Concurrent_stale_writer_cannot_overwrite_newer_state()
    {
        // Shared-cache in-memory SQLite is the only provider available here that
        // enforces the relational concurrency token, so the version guard is
        // exercised at the store boundary and not only by its pre-check.
        string connectionString =
            $"Data Source=file:e04_platform_controls_{Guid.NewGuid():N};mode=memory;cache=shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var setup = new FinanceDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.PlatformControlStates.Add(SeededRow());
            await setup.SaveChangesAsync();
        }

        // This context keeps tracking the version-1 row it read.
        await using var staleContext = new FinanceDbContext(options);
        PlatformControlState stale = await staleContext.PlatformControlStates.SingleAsync();
        Assert.Equal(1, stale.Version);

        await using (var currentContext = new FinanceDbContext(options))
        {
            Result<PlatformControlSnapshot> applied = await new PlatformControlStore(currentContext)
                .UpdateAsync(
                    1,
                    new PlatformControlValues(PlatformState.Maintenance, null, "Maintenance", false, false),
                    ActorId,
                    null,
                    default);

            Assert.True(applied.IsSuccess);
            Assert.Equal(2, applied.Value.Version);
        }

        Result<PlatformControlSnapshot> rejected = await new PlatformControlStore(staleContext)
            .UpdateAsync(
                1,
                new PlatformControlValues(PlatformState.Operational, null, null, true, true),
                ActorId,
                null,
                default);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("Platform.ControlVersionConflict", rejected.Error.Code);

        await using var verification = new FinanceDbContext(options);
        PlatformControlState persisted = await verification.PlatformControlStates.AsNoTracking().SingleAsync();
        Assert.Equal(PlatformState.Maintenance, persisted.State);
        Assert.Equal(2, persisted.Version);
        Assert.Equal(1, await verification.PlatformControlAuditEntries.CountAsync());
    }

    [Fact]
    public async Task Admin_read_handler_fails_closed_when_the_record_is_missing_or_unreadable()
    {
        Result<PlatformControlResponse?> missing = await new GetPlatformControlQueryHandler(
            StubReader.Missing()).Handle(new GetPlatformControlQuery(), default);

        Assert.False(missing.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", missing.Error.Code);
        Assert.Equal("Platform control state is unavailable.", missing.Error.Message);

        Result<PlatformControlResponse?> failed = await new GetPlatformControlQueryHandler(
            StubReader.Failing(new InvalidOperationException("finance connection failed"))).Handle(
                new GetPlatformControlQuery(), default);

        Assert.False(failed.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", failed.Error.Code);
        Assert.DoesNotContain("finance connection failed", failed.Error.Message);

        Result<PlatformControlResponse?> mapped = await new GetPlatformControlQueryHandler(
            StubReader.With(Snapshot())).Handle(new GetPlatformControlQuery(), default);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(1, mapped.Value!.Version);
        Assert.True(mapped.Value.CardEnabled);
    }

    [Fact]
    public async Task Update_handler_maps_writer_unavailability_conflict_and_invalidity()
    {
        Result<PlatformControlResponse> unavailable = await new UpdatePlatformControlCommandHandler(
            new StubWriter(Result<PlatformControlSnapshot>.Failure(PlatformControlErrors.Unavailable)))
            .Handle(Command(), default);
        Assert.False(unavailable.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", unavailable.Error.Code);

        Result<PlatformControlResponse> conflict = await new UpdatePlatformControlCommandHandler(
            new StubWriter(Result<PlatformControlSnapshot>.Failure(PlatformControlErrors.VersionConflict)))
            .Handle(Command(), default);
        Assert.False(conflict.IsSuccess);
        Assert.Equal("Platform.ControlVersionConflict", conflict.Error.Code);

        Result<PlatformControlResponse> invalid = await new UpdatePlatformControlCommandHandler(
            new ThrowingWriter()).Handle(Command(), default);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", invalid.Error.Code);

        Result<PlatformControlResponse> applied = await new UpdatePlatformControlCommandHandler(
            new StubWriter(Result<PlatformControlSnapshot>.Success(Snapshot(version: 2))))
            .Handle(Command(), default);
        Assert.True(applied.IsSuccess);
        Assert.Equal(2, applied.Value.Version);
    }

    [Fact]
    public async Task Public_status_fails_closed_when_the_control_record_cannot_be_read()
    {
        Result<PublicPlatformStatusResponse> missing = await new GetPublicPlatformStatusQueryHandler(
            StubReader.Missing(), new StubConfiguration(true, true)).Handle(
                new GetPublicPlatformStatusQuery(), default);

        Assert.False(missing.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", missing.Error.Code);

        Result<PublicPlatformStatusResponse> failed = await new GetPublicPlatformStatusQueryHandler(
            StubReader.Failing(new TimeoutException("gateway timeout")),
            new StubConfiguration(true, true)).Handle(new GetPublicPlatformStatusQuery(), default);

        Assert.False(failed.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", failed.Error.Code);
        Assert.DoesNotContain("gateway timeout", failed.Error.Message);
    }

    [Theory]
    [InlineData(true, true, true, true, new[] { 1, 2 })]
    [InlineData(true, true, true, false, new[] { 1 })]
    [InlineData(true, true, false, true, new[] { 2 })]
    [InlineData(true, true, false, false, new int[0])]
    [InlineData(true, false, true, true, new[] { 1 })]
    [InlineData(false, true, true, true, new[] { 2 })]
    [InlineData(false, false, true, true, new int[0])]
    public async Task Public_status_advertises_only_enabled_and_configured_methods(
        bool cardEnabled,
        bool walletEnabled,
        bool cardConfigured,
        bool walletConfigured,
        int[] expected)
    {
        Result<PublicPlatformStatusResponse> result = await new GetPublicPlatformStatusQueryHandler(
            StubReader.With(Snapshot(cardEnabled: cardEnabled, walletEnabled: walletEnabled)),
            new StubConfiguration(cardConfigured, walletConfigured)).Handle(
                new GetPublicPlatformStatusQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            expected,
            result.Value.AvailablePaymentMethods.Select(method => (int)method).ToArray());
    }

    [Fact]
    public async Task Public_status_ignores_the_subscription_enrollment_prerequisite()
    {
        // The path-specific Card enrollment prerequisite never changes the
        // advertised method list: that list stays driven by the generic Card
        // truth and the enabled switches only.
        Result<PublicPlatformStatusResponse> result = await new GetPublicPlatformStatusQueryHandler(
            StubReader.With(Snapshot()),
            new StubConfiguration(true, true, cardSubscriptionEnrollmentConfigured: false)).Handle(
                new GetPublicPlatformStatusQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [PlatformPaymentMethod.Card, PlatformPaymentMethod.Wallet],
            result.Value.AvailablePaymentMethods.ToArray());
    }

    [Fact]
    public void Payment_methods_are_limited_to_the_implemented_card_and_wallet()
    {
        Assert.Equal(
            [PlatformPaymentMethod.Card, PlatformPaymentMethod.Wallet],
            Enum.GetValues<PlatformPaymentMethod>());
        Assert.DoesNotContain(
            Enum.GetNames<PlatformPaymentMethod>(),
            name => name.Contains("Apple", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [PlatformState.Operational, PlatformState.Maintenance],
            Enum.GetValues<PlatformState>());
    }

    [Fact]
    public void Public_status_response_shape_carries_no_internal_control_data()
    {
        string[] properties = typeof(PublicPlatformStatusResponse)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["AvailablePaymentMethods", "MaintenanceMessageArabic", "MaintenanceMessageEnglish", "State"],
            properties);
        Assert.DoesNotContain(
            properties,
            name => name.Contains("Version", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Reason", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Actor", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Finance_registration_exposes_the_control_reader_writer_and_keeps_migration_startup_off()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:FinanceDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test")
            ]).Build();

        services.AddFinanceInfrastructure(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IPlatformControlReader reader = scope.ServiceProvider.GetRequiredService<IPlatformControlReader>();
        IPlatformControlWriter writer = scope.ServiceProvider.GetRequiredService<IPlatformControlWriter>();

        Assert.Same(reader, writer);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PlatformControlStore>());
        Assert.False(
            scope.ServiceProvider.GetRequiredService<IOptions<FinanceMigrationOptions>>()
                .Value.ApplyOnStartup);
    }

    private static FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PlatformControlState SeededRow(
        PlatformState state = PlatformState.Operational,
        bool cardEnabled = true,
        bool walletEnabled = true,
        string? messageArabic = null,
        string? messageEnglish = null) => PlatformControlState.Create(
        SeedId, state, messageArabic, messageEnglish, cardEnabled, walletEnabled, 1, Utc(1));

    private static PlatformControlSnapshot Snapshot(
        int version = 1,
        bool cardEnabled = true,
        bool walletEnabled = true) =>
        new(
            SeedId,
            new PlatformControlValues(
                PlatformState.Operational, null, null, cardEnabled, walletEnabled),
            version,
            Utc(1));

    private static UpdatePlatformControlCommand Command() =>
        new(1, PlatformState.Operational, null, null, true, true, ActorId);

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class StubReader : IPlatformControlReader
    {
        private readonly PlatformControlSnapshot? _snapshot;
        private readonly Exception? _failure;

        private StubReader(PlatformControlSnapshot? snapshot, Exception? failure)
        {
            _snapshot = snapshot;
            _failure = failure;
        }

        public static StubReader Missing() => new(null, null);

        public static StubReader Failing(Exception failure) => new(null, failure);

        public static StubReader With(PlatformControlSnapshot snapshot) => new(snapshot, null);

        public Task<PlatformControlSnapshot?> GetAsync(CancellationToken cancellationToken) =>
            _failure is null
                ? Task.FromResult(_snapshot)
                : Task.FromException<PlatformControlSnapshot?>(_failure);
    }

    private sealed class StubConfiguration(
        bool cardConfigured,
        bool walletConfigured,
        bool cardSubscriptionEnrollmentConfigured = true)
        : IPaymentMethodConfiguration
    {
        public bool CardConfigured => cardConfigured;
        public bool WalletConfigured => walletConfigured;

        // Only the path-specific subscription-enrollment branch reads this; the
        // public status response under test is driven by the generic Card truth.
        public bool CardSubscriptionEnrollmentConfigured => cardSubscriptionEnrollmentConfigured;
    }

    private sealed class StubWriter(Result<PlatformControlSnapshot> result) : IPlatformControlWriter
    {
        public Task<Result<PlatformControlSnapshot>> UpdateAsync(
            int expectedVersion,
            PlatformControlValues values,
            Guid actorUserId,
            string? internalReason,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ThrowingWriter : IPlatformControlWriter
    {
        public Task<Result<PlatformControlSnapshot>> UpdateAsync(
            int expectedVersion,
            PlatformControlValues values,
            Guid actorUserId,
            string? internalReason,
            CancellationToken cancellationToken) =>
            Task.FromException<Result<PlatformControlSnapshot>>(
                new InvalidOperationException("finance store unavailable"));
    }
}

/// <summary>
/// E04-A authored migration and model mapping. The migration is authored and
/// deliberately not applied: these assertions read the compiled migration
/// operations and the design-time model without connecting to any database.
/// </summary>
public sealed class PlatformControlPersistenceModelTests
{
    private const string MigrationId = "20261019061622_AddE04PlatformControls";

    private static readonly Guid SeedId =
        Guid.Parse("0198e2c2-4444-7777-8888-0000000000e4");

    [Fact]
    public void Model_maps_control_and_audit_tables_into_the_finance_schema()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("finance", FinanceDbContext.Schema);

        IEntityType control = model.FindEntityType(typeof(PlatformControlState))!;
        Assert.Equal("finance", control.GetSchema());
        Assert.Equal("platform_control_states", control.GetTableName());
        Assert.Equal("version", Column(control, nameof(PlatformControlState.Version)));
        Assert.True(control.FindProperty(nameof(PlatformControlState.Version))!.IsConcurrencyToken);
        Assert.Equal("state", Column(control, nameof(PlatformControlState.State)));
        Assert.Equal(
            "maintenance_message_arabic",
            Column(control, nameof(PlatformControlState.MaintenanceMessageArabic)));
        Assert.Equal("card_enabled", Column(control, nameof(PlatformControlState.CardEnabled)));
        Assert.Equal("wallet_enabled", Column(control, nameof(PlatformControlState.WalletEnabled)));
        Assert.Equal("updated_on_utc", Column(control, nameof(PlatformControlState.UpdatedOnUtc)));
        Assert.Contains(
            control.GetCheckConstraints(),
            constraint => constraint.ModelName == "ck_platform_control_states_version");
        Assert.Contains(
            control.GetCheckConstraints(),
            constraint => constraint.ModelName == "ck_platform_control_states_state");

        IIndex versionIndex = Assert.Single(control.GetIndexes());
        Assert.Equal("ux_platform_control_states_version", versionIndex.GetDatabaseName());
        Assert.True(versionIndex.IsUnique);

        // The single initial row is authored in the migration, not as model
        // seed data, so an isolated fixture can still represent an unreadable
        // (missing) control row without the model re-creating it.
        Assert.Empty(control.GetSeedData());

        IEntityType audit = model.FindEntityType(typeof(PlatformControlAuditEntry))!;
        Assert.Equal("finance", audit.GetSchema());
        Assert.Equal("platform_control_audit_entries", audit.GetTableName());
        Assert.Equal("actor_user_id", Column(audit, nameof(PlatformControlAuditEntry.ActorUserId)));
        Assert.Equal("previous_version", Column(audit, nameof(PlatformControlAuditEntry.PreviousVersion)));
        Assert.Equal("new_version", Column(audit, nameof(PlatformControlAuditEntry.NewVersion)));
        Assert.Equal("before_state", Column(audit, nameof(PlatformControlAuditEntry.BeforeState)));
        Assert.Equal("after_state", Column(audit, nameof(PlatformControlAuditEntry.AfterState)));
        Assert.Equal(
            "before_card_enabled",
            Column(audit, nameof(PlatformControlAuditEntry.BeforeCardEnabled)));
        Assert.Equal(
            "after_wallet_enabled",
            Column(audit, nameof(PlatformControlAuditEntry.AfterWalletEnabled)));
        Assert.Equal(
            "before_maintenance_message_arabic",
            Column(audit, nameof(PlatformControlAuditEntry.BeforeMaintenanceMessageArabic)));
        Assert.Equal(
            "after_maintenance_message_arabic",
            Column(audit, nameof(PlatformControlAuditEntry.AfterMaintenanceMessageArabic)));
        Assert.Equal(
            "before_maintenance_message_english",
            Column(audit, nameof(PlatformControlAuditEntry.BeforeMaintenanceMessageEnglish)));
        Assert.Equal(
            "after_maintenance_message_english",
            Column(audit, nameof(PlatformControlAuditEntry.AfterMaintenanceMessageEnglish)));
        Assert.All(
            new[]
            {
                nameof(PlatformControlAuditEntry.BeforeMaintenanceMessageArabic),
                nameof(PlatformControlAuditEntry.AfterMaintenanceMessageArabic),
                nameof(PlatformControlAuditEntry.BeforeMaintenanceMessageEnglish),
                nameof(PlatformControlAuditEntry.AfterMaintenanceMessageEnglish)
            },
            property => Assert.True(audit.FindProperty(property)!.IsNullable));
        Assert.Equal("internal_reason", Column(audit, nameof(PlatformControlAuditEntry.InternalReason)));
        Assert.Equal("occurred_on_utc", Column(audit, nameof(PlatformControlAuditEntry.OccurredOnUtc)));
        Assert.Contains(
            audit.GetCheckConstraints(),
            constraint => constraint.ModelName == "ck_platform_control_audit_entries_versions");
        Assert.Contains(
            audit.GetCheckConstraints(),
            constraint => constraint.ModelName == "ck_platform_control_audit_entries_states");
        Assert.Empty(audit.GetIndexes());
        Assert.Empty(audit.GetSeedData());
        Assert.DoesNotContain(audit.GetProperties(), property => property.IsConcurrencyToken);
    }

    [Fact]
    public void Model_snapshot_already_describes_the_current_model()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        string[] currentTables = TablesOf(context.GetService<IDesignTimeModel>().Model);
        string[] snapshotTables = TablesOf(migrations.ModelSnapshot!.Model);

        Assert.Contains("platform_control_states", currentTables);
        Assert.Contains("platform_control_audit_entries", currentTables);
        Assert.Equal(currentTables, snapshotTables);
    }

    [Fact]
    public void Authored_migration_creates_the_two_tables_with_one_operational_seed_row()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        Assert.Contains(MigrationId, migrations.Migrations.Keys);
        Type migrationType = migrations.Migrations[MigrationId].AsType();
        Assert.Equal("AddE04PlatformControls", migrationType.Name);
        Assert.Equal(
            MigrationId,
            migrationType.GetCustomAttribute<MigrationAttribute>()!.Id);

        var migration = (Migration)Activator.CreateInstance(migrationType)!;

        IReadOnlyList<MigrationOperation> up = migration.UpOperations;
        Assert.Equal(
            ["finance.platform_control_audit_entries", "finance.platform_control_states"],
            up.OfType<CreateTableOperation>()
                .Select(operation => $"{operation.Schema}.{operation.Name}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        // The append-only audit table persists the full before/after value set,
        // including the bilingual public maintenance messages.
        CreateTableOperation auditTable = up.OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "platform_control_audit_entries");
        string[] auditColumns = auditTable.Columns
            .Select(column => column.Name)
            .ToArray();
        foreach (string expectedColumn in new[]
        {
            "actor_user_id",
            "before_state",
            "after_state",
            "before_maintenance_message_arabic",
            "after_maintenance_message_arabic",
            "before_maintenance_message_english",
            "after_maintenance_message_english",
            "before_card_enabled",
            "after_card_enabled",
            "before_wallet_enabled",
            "after_wallet_enabled",
            "internal_reason",
            "occurred_on_utc"
        })
        {
            Assert.Contains(expectedColumn, auditColumns);
        }

        Assert.All(
            auditTable.Columns.Where(column =>
                column.Name.Contains("maintenance_message", StringComparison.Ordinal)),
            column => Assert.True(column.IsNullable, $"{column.Name} must be nullable."));

        CreateIndexOperation index = Assert.Single(up.OfType<CreateIndexOperation>());
        Assert.Equal("ux_platform_control_states_version", index.Name);
        Assert.True(index.IsUnique);

        InsertDataOperation seed = Assert.Single(up.OfType<InsertDataOperation>());
        Assert.Equal("finance", seed.Schema);
        Assert.Equal("platform_control_states", seed.Table);
        Assert.Equal(1, seed.Values.GetLength(0));
        Assert.Equal(SeedId, seed.Values[0, Array.IndexOf(seed.Columns, "id")]);
        Assert.Equal(1, seed.Values[0, Array.IndexOf(seed.Columns, "state")]);
        Assert.Null(seed.Values[0, Array.IndexOf(seed.Columns, "maintenance_message_arabic")]);
        Assert.Null(seed.Values[0, Array.IndexOf(seed.Columns, "maintenance_message_english")]);
        Assert.Equal(true, seed.Values[0, Array.IndexOf(seed.Columns, "card_enabled")]);
        Assert.Equal(true, seed.Values[0, Array.IndexOf(seed.Columns, "wallet_enabled")]);
        Assert.Equal(1, seed.Values[0, Array.IndexOf(seed.Columns, "version")]);
        Assert.Equal(
            new DateTime(2026, 10, 19, 6, 16, 22, 0, DateTimeKind.Utc),
            seed.Values[0, Array.IndexOf(seed.Columns, "updated_on_utc")]);

        // Exactly the two tables, one unique index and one seed row: the
        // authored migration carries no unrelated model drift.
        Assert.Equal(4, up.Count);

        IReadOnlyList<MigrationOperation> down = migration.DownOperations;
        DeleteDataOperation seedRemoval = Assert.Single(down.OfType<DeleteDataOperation>());
        Assert.Equal("platform_control_states", seedRemoval.Table);
        Assert.Equal(["id"], seedRemoval.KeyColumns);
        Assert.Equal([SeedId], seedRemoval.KeyValues.Cast<object?>().ToArray());
        Assert.Equal(2, down.OfType<DropTableOperation>().Count());
    }

    private static string? Column(IEntityType entityType, string propertyName) =>
        entityType.FindProperty(propertyName)!.GetColumnName();

    private static string[] TablesOf(IModel model) => model.GetEntityTypes()
        .Select(entity => entity.GetTableName())
        .Where(name => name is not null)
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static FinanceDbContext CreateNpgsqlContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=e04_model_only;Username=unused;Password=unused")
            .Options);
}
