using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// Provider-level proof for the duplicate-charge active-claim unique index.
/// Uses only a nonce-named PostgreSQL database derived from the verified local
/// integration server, refuses an existing target, applies Finance migrations,
/// and drops only the database this fixture created. It never connects to the
/// source database itself or a remote/Production target.
/// </summary>
[Collection("DuplicateChargeClaimPostgresVerification")]
public sealed class DuplicateChargeClaimPostgresConcurrencyTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "SanadDuplicateClaimVerify_" + Guid.NewGuid().ToString("N");

    private static readonly DateTime NowUtc =
        new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private string? _targetConnectionString;
    private bool _createdTarget;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_createdTarget || _targetConnectionString is null)
            return;

        await using FinanceDbContext db = CreateDb(_targetConnectionString);
        await db.Database.EnsureDeletedAsync();
        _createdTarget = false;
        _targetConnectionString = null;
    }

    [DuplicateChargeClaimPostgresFact]
    public async Task Concurrent_submissions_for_one_family_capture_leave_one_live_claim()
    {
        _targetConnectionString = await CreateVerifiedDisposableTargetAsync();

        await using (FinanceDbContext db = CreateDb(_targetConnectionString))
            await db.Database.MigrateAsync();

        Guid familyId = Guid.NewGuid();
        Guid subscriptionId = Guid.NewGuid();
        Guid paymentAttemptId = Guid.NewGuid();
        var gate = new SaveChangesGate(2);
        DbContextOptions<FinanceDbContext> options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(
                _targetConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(gate)
            .Options;

        Task<WriteResult> first = InsertClaimAsync(
            options, familyId, subscriptionId, paymentAttemptId, "claim-a");
        Task<WriteResult> second = InsertClaimAsync(
            options, familyId, subscriptionId, paymentAttemptId, "claim-b");
        WriteResult[] results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Saved);
        DbUpdateException conflict = Assert.IsType<DbUpdateException>(
            Assert.Single(results, result => !result.Saved).Error);
        Assert.NotNull(conflict);

        await using FinanceDbContext verify = CreateDb(_targetConnectionString);
        Assert.Equal(
            1,
            await verify.DuplicateChargeClaims.CountAsync(claim =>
                claim.FamilyAccountId == familyId &&
                claim.PaymentAttemptId == paymentAttemptId &&
                claim.State != DuplicateChargeClaimState.Rejected));
    }

    private static async Task<WriteResult> InsertClaimAsync(
        DbContextOptions<FinanceDbContext> options,
        Guid familyId,
        Guid subscriptionId,
        Guid paymentAttemptId,
        string key)
    {
        await using FinanceDbContext db = new(options);
        db.DuplicateChargeClaims.Add(DuplicateChargeClaim.Submit(
            familyId,
            subscriptionId,
            paymentAttemptId,
            Guid.NewGuid(),
            basisMinorUnits: 10_000,
            NowUtc,
            evidenceReference: $"provider evidence {key}",
            idempotencyKey: key,
            submittedOnUtc: NowUtc));

        try
        {
            await db.SaveChangesAsync();
            return new(Saved: true, Error: null);
        }
        catch (DbUpdateException exception)
        {
            return new(Saved: false, Error: exception);
        }
    }

    private async Task<string> CreateVerifiedDisposableTargetAsync()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"PostgreSQL proof requires {ConnectionStringVariable} to be set.");

        var target = new NpgsqlConnectionStringBuilder(raw);
        if (target.Host is not ("localhost" or "127.0.0.1" or "::1") || target.Port != 5432)
            throw new InvalidOperationException(
                "Refusing to write: duplicate-claim PostgreSQL proof requires loopback port 5432.");

        target.Database = ExpectedDatabase;
        if (!string.Equals(target.Database, ExpectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to write to an unexpected disposable database.");

        var maintenance = new NpgsqlConnectionStringBuilder(target.ConnectionString)
        {
            Database = "postgres"
        };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using (NpgsqlCommand exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
            exists.Parameters.AddWithValue("database", ExpectedDatabase);
            if ((bool)(await exists.ExecuteScalarAsync() ?? false))
                throw new InvalidOperationException(
                    "Refusing to write: the nonce-named duplicate-claim target already exists.");
        }

        await using (NpgsqlCommand create = connection.CreateCommand())
        {
            // Identifier is generated locally from a fixed prefix and Guid N format.
            create.CommandText = $"CREATE DATABASE \"{ExpectedDatabase}\"";
            await create.ExecuteNonQueryAsync();
        }

        _createdTarget = true;
        return target.ConnectionString;
    }

    private static FinanceDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", FinanceDbContext.Schema))
            .Options);

    private sealed record WriteResult(bool Saved, Exception? Error);

    private sealed class SaveChangesGate(int expectedArrivals) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == expectedArrivals)
                _released.TrySetResult(true);

            await _released.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}

[CollectionDefinition("DuplicateChargeClaimPostgresVerification", DisableParallelization = true)]
public sealed class DuplicateChargeClaimPostgresVerificationCollection;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DuplicateChargeClaimPostgresFactAttribute : FactAttribute
{
    public DuplicateChargeClaimPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ConnectionStrings__IdentityIntegrationDatabase")))
        {
            Skip = "Duplicate-charge PostgreSQL proof skipped; set ConnectionStrings__IdentityIntegrationDatabase to a local integration server.";
        }
    }
}
