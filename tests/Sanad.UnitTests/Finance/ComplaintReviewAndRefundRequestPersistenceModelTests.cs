using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// W3-F3 persistence mapping and the additive migration. The migration is
/// authored and deliberately NOT applied: these assertions read the compiled
/// migration operations, the design-time model and the model snapshot without
/// connecting to any database. It is a files-only migration slot after
/// <c>20261022210000_CreateFinanceMoneyCore</c>.
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class ComplaintReviewAndRefundRequestPersistenceModelTests
{
    private const string MigrationId = "20261022230000_AddComplaintReviewsAndSubscriptionRefundRequests";
    private const string PredecessorMigrationId = "20261022210000_CreateFinanceMoneyCore";

    [Fact]
    public void The_new_tables_map_into_the_finance_schema()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType review = model.FindEntityType(typeof(ComplaintReview))!;
        Assert.Equal("finance", review.GetSchema());
        Assert.Equal("complaint_reviews", review.GetTableName());
        Assert.Equal("refund_case_id", review.FindProperty(nameof(ComplaintReview.RefundCaseId))!.GetColumnName());
        Assert.Equal("state", review.FindProperty(nameof(ComplaintReview.State))!.GetColumnName());
        Assert.Equal("timeliness", review.FindProperty(nameof(ComplaintReview.Timeliness))!.GetColumnName());
        Assert.Equal("reported_by_user_id", review.FindProperty(nameof(ComplaintReview.ReportedByUserId))!.GetColumnName());

        IEntityType request = model.FindEntityType(typeof(SubscriptionRefundRequest))!;
        Assert.Equal("finance", request.GetSchema());
        Assert.Equal("subscription_refund_requests", request.GetTableName());
        Assert.Equal("family_id", request.FindProperty(nameof(SubscriptionRefundRequest.FamilyId))!.GetColumnName());
        Assert.Equal("idempotency_key", request.FindProperty(nameof(SubscriptionRefundRequest.IdempotencyKey))!.GetColumnName());
        Assert.Equal("requested_amount_minor_units",
            request.FindProperty(nameof(SubscriptionRefundRequest.RequestedAmountMinorUnits))!.GetColumnName());
        Assert.Equal("subscription_id",
            request.FindProperty(nameof(SubscriptionRefundRequest.SubscriptionId))!.GetColumnName());
        Assert.Equal(SubscriptionRefundRequest.MaximumIdempotencyKeyLength,
            request.FindProperty(nameof(SubscriptionRefundRequest.IdempotencyKey))!.GetMaxLength());

        IEntityType budget = model.FindEntityType(typeof(SubscriptionRefundBudget))!;
        Assert.Equal("finance", budget.GetSchema());
        Assert.Equal("subscription_refund_budgets", budget.GetTableName());
        Assert.Equal("payment_attempt_id",
            budget.FindProperty(nameof(SubscriptionRefundBudget.PaymentAttemptId))!.GetColumnName());
        Assert.Equal("subscription_id", budget.FindProperty(nameof(SubscriptionRefundBudget.SubscriptionId))!.GetColumnName());
        Assert.Equal("captured_minor_units", budget.FindProperty(nameof(SubscriptionRefundBudget.CapturedMinorUnits))!.GetColumnName());
        Assert.Equal("claimed_minor_units", budget.FindProperty(nameof(SubscriptionRefundBudget.ClaimedMinorUnits))!.GetColumnName());
        Assert.Equal("version", budget.FindProperty(nameof(SubscriptionRefundBudget.Version))!.GetColumnName());
        // The runtime projection is not a column.
        Assert.Null(budget.FindProperty(nameof(SubscriptionRefundBudget.RemainingMinorUnits)));
    }

    [Fact]
    public void The_scalar_columns_of_the_projection_are_not_mapped()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType review = model.FindEntityType(typeof(ComplaintReview))!;
        Assert.Null(review.FindProperty(nameof(ComplaintReview.IsUnderInvestigation)));
        Assert.Null(review.FindProperty(nameof(ComplaintReview.ProcessingDeadlineStartsOnUtc)));
    }

    [Fact]
    public void The_review_state_is_a_concurrency_token_with_no_new_column()
    {
        // m6: lost-update protection follows the credit-balance precedent — the
        // existing state column is the token, so no schema change is introduced
        // and the DbUpdateConcurrencyException -> Finance.Complaint.Conflict catch
        // in the store stays reachable.
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType review = model.FindEntityType(typeof(ComplaintReview))!;
        IProperty state = review.FindProperty(nameof(ComplaintReview.State))!;

        Assert.True(state.IsConcurrencyToken);
        Assert.Equal("state", state.GetColumnName());
    }

    [Fact]
    public void Check_constraints_pin_the_review_and_idempotency_invariants()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType review = model.FindEntityType(typeof(ComplaintReview))!;
        foreach (string constraint in new[]
        {
            "ck_complaint_reviews_state",
            "ck_complaint_reviews_timeliness",
            "ck_complaint_reviews_reporting_party",
            "ck_complaint_reviews_resolution_attributable",
            "ck_complaint_reviews_single_verdict"
        })
        {
            Assert.Contains(review.GetCheckConstraints(), item => item.ModelName == constraint);
        }

        IEntityType request = model.FindEntityType(typeof(SubscriptionRefundRequest))!;
        foreach (string constraint in new[]
        {
            "ck_subscription_refund_requests_reason",
            "ck_subscription_refund_requests_amount",
            "ck_subscription_refund_requests_key_length"
        })
        {
            Assert.Contains(request.GetCheckConstraints(), item => item.ModelName == constraint);
        }

        IEntityType budget = model.FindEntityType(typeof(SubscriptionRefundBudget))!;
        foreach (string constraint in new[]
        {
            "ck_subscription_refund_budgets_captured",
            "ck_subscription_refund_budgets_claimed",
            "ck_subscription_refund_budgets_within_captured",
            "ck_subscription_refund_budgets_version"
        })
        {
            Assert.Contains(budget.GetCheckConstraints(), item => item.ModelName == constraint);
        }
    }

    [Fact]
    public void The_budget_version_is_the_shared_serialization_token()
    {
        // M4: two concurrent awards against the same CAPTURE budget cannot both
        // commit, because the second update fails its
        // `WHERE version = @original` predicate. N1: the token is mapped, not
        // merely declared on the entity.
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType budget = model.FindEntityType(typeof(SubscriptionRefundBudget))!;
        IProperty version = budget.FindProperty(nameof(SubscriptionRefundBudget.Version))!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("version", version.GetColumnName());
    }

    [Fact]
    public void One_review_per_case_and_one_request_per_family_key_are_unique()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IIndex caseIndex = Assert.Single(
            model.FindEntityType(typeof(ComplaintReview))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_complaint_reviews_refund_case_id");
        Assert.True(caseIndex.IsUnique);

        IIndex keyIndex = Assert.Single(
            model.FindEntityType(typeof(SubscriptionRefundRequest))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_subscription_refund_requests_family_idempotency_key");
        Assert.True(keyIndex.IsUnique);
        Assert.Equal(
            new[] { nameof(SubscriptionRefundRequest.FamilyId), nameof(SubscriptionRefundRequest.IdempotencyKey) },
            keyIndex.Properties.Select(property => property.Name).ToArray());

        IIndex caseRequestIndex = Assert.Single(
            model.FindEntityType(typeof(SubscriptionRefundRequest))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_subscription_refund_requests_refund_case_id");
        Assert.True(caseRequestIndex.IsUnique);

        // One budget per CAPTURE: the row every clause-9 refund case against that
        // paid period serializes on.
        IIndex captureIndex = Assert.Single(
            model.FindEntityType(typeof(SubscriptionRefundBudget))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_subscription_refund_budgets_payment_attempt_id");
        Assert.True(captureIndex.IsUnique);
        Assert.Equal(
            nameof(SubscriptionRefundBudget.PaymentAttemptId),
            Assert.Single(captureIndex.Properties).Name);

        // The subscription is audit correlation only: a subscription owns many
        // captures, so its index must not be unique (a unique index here would be
        // the old subscription-scoped ceiling and would reject the second capture).
        IIndex auditIndex = Assert.Single(
            model.FindEntityType(typeof(SubscriptionRefundBudget))!.GetIndexes(),
            index => index.GetDatabaseName() == "ix_subscription_refund_budgets_subscription_id");
        Assert.False(auditIndex.IsUnique);
        Assert.Equal(
            nameof(SubscriptionRefundBudget.SubscriptionId),
            Assert.Single(auditIndex.Properties).Name);
    }

    [Fact]
    public void Model_snapshot_describes_the_current_model_in_lockstep()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        string[] currentTables = TablesOf(context.GetService<IDesignTimeModel>().Model);
        string[] snapshotTables = TablesOf(migrations.ModelSnapshot!.Model);

        Assert.Contains("complaint_reviews", currentTables);
        Assert.Contains("subscription_refund_requests", currentTables);
        Assert.Contains("subscription_refund_budgets", currentTables);
        // The predecessor money-core tables remain.
        Assert.Contains("refund_ledgers", currentTables);
        Assert.Contains("refund_ledger_entries", currentTables);
        Assert.Contains("credit_balances", currentTables);
        Assert.Equal(currentTables, snapshotTables);
    }

    [Fact]
    public void The_migration_is_additive_after_the_money_core_migration()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        Assert.Contains(PredecessorMigrationId, migrations.Migrations.Keys);
        Assert.Contains(MigrationId, migrations.Migrations.Keys);
        Type migrationType = migrations.Migrations[MigrationId].AsType();
        Assert.Equal("AddComplaintReviewsAndSubscriptionRefundRequests", migrationType.Name);
        Assert.Equal(MigrationId, migrationType.GetCustomAttribute<MigrationAttribute>()!.Id);

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        IReadOnlyList<MigrationOperation> up = migration.UpOperations;

        Assert.Equal(
            ["finance.complaint_reviews", "finance.subscription_refund_budgets", "finance.subscription_refund_requests"],
            up.OfType<CreateTableOperation>()
                .Select(operation => $"{operation.Schema}.{operation.Name}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        // The additive slot creates tables only: it never drops or alters an
        // existing money-core table.
        Assert.Empty(up.OfType<DropTableOperation>());
        Assert.Empty(up.OfType<AlterColumnOperation>());
        Assert.Empty(up.OfType<DropColumnOperation>());
        Assert.Empty(up.OfType<InsertDataOperation>());

        CreateTableOperation requests = up.OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "subscription_refund_requests");
        string[] requestColumns = requests.Columns.Select(column => column.Name).ToArray();
        foreach (string expected in new[]
        {
            "id", "refund_case_id", "family_id", "subscription_id", "idempotency_key",
            "requested_amount_minor_units", "reason", "evidence_reference", "created_on_utc"
        })
        {
            Assert.Contains(expected, requestColumns);
        }

        CreateTableOperation budgets = up.OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "subscription_refund_budgets");
        string[] budgetColumns = budgets.Columns.Select(column => column.Name).ToArray();
        Assert.Equal(
            ["id", "payment_attempt_id", "subscription_id", "family_id", "captured_minor_units",
             "claimed_minor_units", "version", "created_on_utc", "updated_on_utc"],
            budgetColumns);

        // M4: the budget is unique on the CAPTURE and non-unique on the
        // subscription (audit only). The subscription-scoped unique index must not
        // exist, because it would cap a subscription at one budget and reject the
        // second captured period.
        CreateIndexOperation captureIndex = up.OfType<CreateIndexOperation>()
            .Single(operation => operation.Name == "ux_subscription_refund_budgets_payment_attempt_id");
        Assert.True(captureIndex.IsUnique);
        Assert.Equal(["payment_attempt_id"], captureIndex.Columns);

        CreateIndexOperation auditIndex = up.OfType<CreateIndexOperation>()
            .Single(operation => operation.Name == "ix_subscription_refund_budgets_subscription_id");
        Assert.False(auditIndex.IsUnique);
        Assert.Equal(["subscription_id"], auditIndex.Columns);

        Assert.DoesNotContain(
            up.OfType<CreateIndexOperation>(),
            operation => operation.Name == "ux_subscription_refund_budgets_subscription_id");
    }

    [Fact]
    public void The_migration_down_drops_only_the_three_new_tables()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        var migration = (Migration)Activator.CreateInstance(migrations.Migrations[MigrationId].AsType())!;

        Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    private static string[] TablesOf(IModel model) => model.GetEntityTypes()
        .Select(entity => entity.GetTableName())
        .Where(name => name is not null)
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static FinanceDbContext CreateNpgsqlContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=w3f3_model_only;Username=unused;Password=unused")
            .Options);
}
