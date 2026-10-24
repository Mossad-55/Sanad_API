using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// W4-F3 relational proof for the plan-change double-refund correction against a
/// validated disposable PostgreSQL target.
/// <para>
/// The scenario is the F3 fixture: the initial capture settled 342.00 for the
/// period, then a mid-period paid plan change charged an incremental 300.00
/// (target gross 400.00 minus the 100.00 prorated credit), and both captures
/// settle inside the same period. The shutdown handler must attribute the
/// plan-change capture to its own 300.00 of NEW money, not to the 400.00 target
/// gross that embeds the credit already covered by the source capture's budget.
/// The Finance recorder must then keep two independent budgets: the plan-change
/// capture at 300.00 (150.00 at the period midpoint) and the source capture at
/// 342.00 (full, duplicate-charge). Running the source write first must not
/// change either ceiling.
/// </para>
/// <para>
/// Safety: the target is derived from
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>, whose host must be
/// loopback on port 5432; the database name is replaced with this process's
/// unique nonce-bearing name and verified absent before creation. Families and
/// Finance migrations are applied with their own per-schema history tables. This
/// class never connects to the source database or to a persistent database, and
/// it deletes only the unique database it created.
/// </para>
/// <para>
/// NOT RUN: authored for the W4 gate; the Mastermind runs it with the opt-in
/// environment variable set.
/// </para>
/// </summary>
[Collection("W4F3Verification")]
public sealed class SubscriptionRefundPostgresBudgetTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "SanadW4F3Verify_" + Guid.NewGuid().ToString("N");

    private static readonly DateTime CreatedOnUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private static string? _migratedFor;
    private static bool _createdTarget;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_createdTarget || string.IsNullOrWhiteSpace(_migratedFor))
        {
            return;
        }

        // This exact nonce-bearing database was verified absent before this
        // process created it. Cleanup cannot target SanadDb or any fixed-name
        // database.
        await using FinanceDbContext finance = CreateFinanceDb(_migratedFor);
        await finance.Database.EnsureDeletedAsync();
        _createdTarget = false;
        _migratedFor = null;
    }

    [W4F3PostgresFact]
    public async Task Plan_change_shutdown_keeps_the_source_capture_budget_independent()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token);
        Guid actor = Guid.NewGuid();

        // 1. Families resolves the plan-change capture and Finance claims its
        //    own 300.00 budget — the unused half is 150.00, never the 200.00 a
        //    target-gross basis would produce.
        await using (FamiliesDbContext families = CreateFamiliesDb(connectionString))
        await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
        {
            var handler = new RefundSubscriptionServiceShutdownCommandHandler(
                families, new SubscriptionRefundRecorder(finance));

            var shutdown = await handler.Handle(
                new RefundSubscriptionServiceShutdownCommand(
                    seed.SubscriptionId, new UserId(actor), seed.MidpointUtc),
                CancellationToken.None);

            Assert.True(shutdown.IsSuccess, shutdown.Error.Message);
            Assert.Equal(15_000L, shutdown.Value.AwardedMinorUnits);
        }

        // 2. The source capture is refunded in full for a duplicate charge under
        //    its own refund case and its own budget.
        await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
        {
            var recorder = new SubscriptionRefundRecorder(finance);

            var fullRefund = await recorder.RecordAsync(
                Guid.NewGuid(),
                seed.FamilyId,
                seed.SubscriptionId,
                seed.InitialAttemptId,
                34_200L,
                Money.FromMinorUnits(34_200L),
                SubscriptionRefundReason.DuplicateCharge,
                token + "-source",
                seed.MidpointUtc,
                actor,
                evidenceReference: null,
                CancellationToken.None);

            Assert.True(fullRefund.IsSuccess, fullRefund.Error.Message);
            Assert.Equal(34_200L, fullRefund.Value.AwardedMinorUnits);
        }

        // 3. The two budgets stay independent: writing the 342.00 source capture
        //    does not grow the plan-change ceiling, and the credit the target
        //    gross embedded is never refundable a second time.
        await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
        {
            SubscriptionRefundBudget planChangeBudget = await finance.SubscriptionRefundBudgets
                .AsNoTracking()
                .SingleAsync(item => item.PaymentAttemptId == seed.PlanChangeAttemptId);
            SubscriptionRefundBudget sourceBudget = await finance.SubscriptionRefundBudgets
                .AsNoTracking()
                .SingleAsync(item => item.PaymentAttemptId == seed.InitialAttemptId);

            Assert.Equal(30_000L, planChangeBudget.CapturedMinorUnits);
            Assert.Equal(15_000L, planChangeBudget.ClaimedMinorUnits);
            Assert.Equal(15_000L, planChangeBudget.RemainingMinorUnits);

            Assert.Equal(34_200L, sourceBudget.CapturedMinorUnits);
            Assert.Equal(34_200L, sourceBudget.ClaimedMinorUnits);
            Assert.Equal(0L, sourceBudget.RemainingMinorUnits);

            // The plan-change capture is exhausted after one more 150.00; a
            // 150.01 claim is refused and records nothing.
            var recorder = new SubscriptionRefundRecorder(finance);
            var overClaim = await recorder.RecordAsync(
                Guid.NewGuid(),
                seed.FamilyId,
                seed.SubscriptionId,
                seed.PlanChangeAttemptId,
                30_000L,
                Money.FromMinorUnits(15_001L),
                SubscriptionRefundReason.ServiceShutdown,
                token + "-over-claim",
                seed.MidpointUtc,
                actor,
                evidenceReference: null,
                CancellationToken.None);

            Assert.False(overClaim.IsSuccess);
            Assert.Equal(Sanad.Modules.Finance.Application.SubscriptionRefundErrors.ExceedsCaptured, overClaim.Error);
        }
    }

    [W4F3PostgresFact]
    public async Task Source_capture_refund_first_does_not_widen_the_plan_change_budget()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token);
        Guid actor = Guid.NewGuid();

        // Reverse order: the full 342.00 source refund commits first.
        await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
        {
            var recorder = new SubscriptionRefundRecorder(finance);
            var fullRefund = await recorder.RecordAsync(
                Guid.NewGuid(),
                seed.FamilyId,
                seed.SubscriptionId,
                seed.InitialAttemptId,
                34_200L,
                Money.FromMinorUnits(34_200L),
                SubscriptionRefundReason.DuplicateCharge,
                token + "-source-first",
                seed.MidpointUtc,
                actor,
                evidenceReference: null,
                CancellationToken.None);

            Assert.True(fullRefund.IsSuccess, fullRefund.Error.Message);
        }

        // The plan-change shutdown still claims only its own 300.00 basis.
        await using (FamiliesDbContext families = CreateFamiliesDb(connectionString))
        await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
        {
            var handler = new RefundSubscriptionServiceShutdownCommandHandler(
                families, new SubscriptionRefundRecorder(finance));
            var shutdown = await handler.Handle(
                new RefundSubscriptionServiceShutdownCommand(
                    seed.SubscriptionId, new UserId(actor), seed.MidpointUtc),
                CancellationToken.None);

            Assert.True(shutdown.IsSuccess, shutdown.Error.Message);
            Assert.Equal(15_000L, shutdown.Value.AwardedMinorUnits);
        }

        await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
        {
            SubscriptionRefundBudget planChangeBudget = await finance.SubscriptionRefundBudgets
                .AsNoTracking()
                .SingleAsync(item => item.PaymentAttemptId == seed.PlanChangeAttemptId);
            SubscriptionRefundBudget sourceBudget = await finance.SubscriptionRefundBudgets
                .AsNoTracking()
                .SingleAsync(item => item.PaymentAttemptId == seed.InitialAttemptId);

            Assert.Equal(30_000L, planChangeBudget.CapturedMinorUnits);
            Assert.Equal(15_000L, planChangeBudget.ClaimedMinorUnits);
            Assert.Equal(34_200L, sourceBudget.CapturedMinorUnits);
            Assert.Equal(34_200L, sourceBudget.ClaimedMinorUnits);
        }
    }

    private static async Task<Seed> SeedAsync(string connectionString, string token)
    {
        await using FamiliesDbContext db = CreateFamiliesDb(connectionString);

        UserId owner = UserId.New();
        Family family = Family.Create(owner, $"W4-F3 {token}");
        SubscriptionPlanVersion plan = SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            isPublished: true,
            createdOnUtc: CreatedOnUtc,
            publishedOnUtc: CreatedOnUtc);
        DateTime periodEndsOnUtc = CreatedOnUtc.AddMonths(1);
        FamilySubscription subscription = FamilySubscription.Create(
            family.Id, plan, CreatedOnUtc, periodEndsOnUtc);
        db.Families.Add(family);
        db.SubscriptionPlanVersions.Add(plan);
        db.FamilySubscriptions.Add(subscription);

        // Initial capture: 342.00 settled for the period.
        SubscriptionPaymentAttempt initial = SubscriptionPaymentAttempt.Create(
            family.Id, plan, 300m, 0m, 0m, 300m, 14m, 42m, 342m,
            null, SubscriptionPaymentMethod.Card, CreatedOnUtc);
        initial.LinkSubscription(subscription.Id);
        initial.TryMarkSucceeded("txn-w4-initial", CreatedOnUtc);
        db.SubscriptionPaymentAttempts.Add(initial);
        subscription.SetCurrentPeriodChargeSnapshot(initial);

        // Paid mid-period plan change: target gross 400.00, 100.00 credit,
        // 300.00 of new money.
        SubscriptionPaymentAttempt planChange = SubscriptionPaymentAttempt.CreatePlanChange(
            subscription,
            plan,
            targetRemainingGross: 400m,
            sourcePeriodGross: 342m,
            proratedCredit: 100m,
            targetTaxRatePercentage: 14m,
            targetTaxAmount: 56m,
            SubscriptionPaymentMethod.Card,
            CreatedOnUtc.AddDays(15));
        planChange.TryMarkSucceeded("txn-w4-plan-change", CreatedOnUtc.AddDays(15));
        db.SubscriptionPaymentAttempts.Add(planChange);
        subscription.ApplyPaidPlanChange(plan, planChange);

        await db.SaveChangesAsync();

        return new Seed(
            FamilyId: family.Id.Value,
            SubscriptionId: subscription.Id,
            InitialAttemptId: initial.Id,
            PlanChangeAttemptId: planChange.Id,
            MidpointUtc: CreatedOnUtc.AddTicks((periodEndsOnUtc - CreatedOnUtc).Ticks / 2));
    }

    private static string NewToken() => "w4-f3-" + Guid.NewGuid().ToString("N");

    private static async Task EnsureMigratedAsync(string connectionString)
    {
        if (_migratedFor == connectionString)
        {
            return;
        }

        await MigrationLock.WaitAsync();
        try
        {
            if (_migratedFor == connectionString)
            {
                return;
            }

            await EnsureFreshDatabaseAbsentAsync(connectionString);

            await using (FamiliesDbContext families = CreateFamiliesDb(connectionString))
            {
                _createdTarget = true;
                await families.Database.MigrateAsync();
            }

            // Finance keeps its migration history inside its own schema, so the
            // two module migration sets never share one history table.
            await using (FinanceDbContext finance = CreateFinanceDb(connectionString))
            {
                await finance.Database.MigrateAsync();
            }

            _migratedFor = connectionString;
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private static string GetValidatedConnectionString()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                $"The W4-F3 relational proof requires {ConnectionStringVariable}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || builder.Port != 5432)
        {
            throw new InvalidOperationException(
                "Refusing to write: the W4-F3 verification source must be localhost:5432 PostgreSQL.");
        }

        builder.Database = ExpectedDatabase;
        if (!string.Equals(builder.Database, ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to write: the W4-F3 verification target must be database '{ExpectedDatabase}'.");
        }

        return builder.ConnectionString;
    }

    private static async Task EnsureFreshDatabaseAbsentAsync(string connectionString)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
        command.Parameters.AddWithValue("database", ExpectedDatabase);
        bool exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        if (exists)
        {
            throw new InvalidOperationException(
                "Refusing to write: the randomly named W4-F3 disposable target already exists; no database was changed.");
        }
    }

    private static FamiliesDbContext CreateFamiliesDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", FamiliesDbContext.Schema))
            .Options);

    private static FinanceDbContext CreateFinanceDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", FinanceDbContext.Schema))
            .Options);

    private sealed record Seed(
        Guid FamilyId,
        Guid SubscriptionId,
        Guid InitialAttemptId,
        Guid PlanChangeAttemptId,
        DateTime MidpointUtc);

    private sealed class W4F3PostgresFactAttribute : FactAttribute
    {
        public W4F3PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "W4-F3 PostgreSQL proof is skipped. Set " +
                       ConnectionStringVariable +
                       " to a local PostgreSQL instance to run it against a " +
                       "uniquely named disposable target.";
            }
        }
    }
}

[CollectionDefinition("W4F3Verification", DisableParallelization = true)]
public sealed class W4F3VerificationCollection
{
}
