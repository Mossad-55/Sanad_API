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
/// W2-F0 persistence mapping and migration. The migration is authored and
/// deliberately NOT applied: these assertions read the compiled migration
/// operations and the design-time model without connecting to any database.
/// Run green at the W2 integration gate by the Mastermind.
/// </summary>
public sealed class FinanceMoneyCorePersistenceModelTests
{
    private const string MigrationId = "20261022210000_CreateFinanceMoneyCore";

    [Fact]
    public void Money_columns_use_the_standardized_numeric_18_2_finance_convention()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType balance = model.FindEntityType(typeof(CreditBalance))!;
        IProperty balanceProperty = balance.FindProperty(nameof(CreditBalance.Balance))!;
        Assert.Equal("numeric(18,2)", balanceProperty.GetColumnType());
        Assert.Equal(18, balanceProperty.GetPrecision());
        Assert.Equal(2, balanceProperty.GetScale());

        IEntityType entry = model.FindEntityType(typeof(RefundLedgerEntry))!;
        IProperty amountProperty = entry.FindProperty(nameof(RefundLedgerEntry.Amount))!;
        Assert.Equal("numeric(18,2)", amountProperty.GetColumnType());
        Assert.Equal(18, amountProperty.GetPrecision());
        Assert.Equal(2, amountProperty.GetScale());
    }

    [Fact]
    public void The_credit_balance_is_an_optimistic_concurrency_token()
    {
        // Lost-update protection for a real financial capability (#15): two
        // concurrent postings cannot silently drop a movement — the loser gets
        // DbUpdateConcurrencyException, mapped to Finance.Credit.Conflict.
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IProperty balanceProperty = model
            .FindEntityType(typeof(CreditBalance))!
            .FindProperty(nameof(CreditBalance.Balance))!;
        Assert.True(balanceProperty.IsConcurrencyToken);
    }

    [Fact]
    public void Money_core_tables_map_into_the_finance_schema()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("finance", FinanceDbContext.Schema);

        IEntityType ledger = model.FindEntityType(typeof(RefundLedger))!;
        Assert.Equal("finance", ledger.GetSchema());
        Assert.Equal("refund_ledgers", ledger.GetTableName());
        Assert.Equal("refund_case_id", ledger.FindProperty(nameof(RefundLedger.RefundCaseId))!.GetColumnName());

        IEntityType entry = model.FindEntityType(typeof(RefundLedgerEntry))!;
        Assert.Equal("finance", entry.GetSchema());
        Assert.Equal("refund_ledger_entries", entry.GetTableName());
        Assert.Equal("entry_type", entry.FindProperty(nameof(RefundLedgerEntry.EntryType))!.GetColumnName());
        Assert.Equal("occurred_on_utc", entry.FindProperty(nameof(RefundLedgerEntry.OccurredOnUtc))!.GetColumnName());
        Assert.Equal("actor_user_id", entry.FindProperty(nameof(RefundLedgerEntry.ActorUserId))!.GetColumnName());
        Assert.Equal("evidence_reference", entry.FindProperty(nameof(RefundLedgerEntry.EvidenceReference))!.GetColumnName());
        Assert.Equal("provider_reference", entry.FindProperty(nameof(RefundLedgerEntry.ProviderReference))!.GetColumnName());

        // The actor is nullable so a system transition is representable; the
        // evidence/provider references are honest nulls when absent.
        Assert.True(entry.FindProperty(nameof(RefundLedgerEntry.ActorUserId))!.IsNullable);
        Assert.True(entry.FindProperty(nameof(RefundLedgerEntry.EvidenceReference))!.IsNullable);
        Assert.True(entry.FindProperty(nameof(RefundLedgerEntry.ProviderReference))!.IsNullable);

        IEntityType balance = model.FindEntityType(typeof(CreditBalance))!;
        Assert.Equal("finance", balance.GetSchema());
        Assert.Equal("credit_balances", balance.GetTableName());
        Assert.Equal("family_id", balance.FindProperty(nameof(CreditBalance.FamilyId))!.GetColumnName());
        Assert.Equal("created_on_utc", balance.FindProperty(nameof(CreditBalance.CreatedOnUtc))!.GetColumnName());
        Assert.Equal("updated_on_utc", balance.FindProperty(nameof(CreditBalance.UpdatedOnUtc))!.GetColumnName());
    }

    [Fact]
    public void Non_negativity_and_precision_are_enforced_by_check_constraints()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType balance = model.FindEntityType(typeof(CreditBalance))!;
        Assert.Contains(
            balance.GetCheckConstraints(),
            constraint => constraint.ModelName == "ck_credit_balances_balance_non_negative");

        IEntityType entry = model.FindEntityType(typeof(RefundLedgerEntry))!;
        Assert.Contains(
            entry.GetCheckConstraints(),
            constraint => constraint.ModelName == "ck_refund_ledger_entries_amount_precision");
    }

    [Fact]
    public void Refund_case_and_family_keys_are_unique()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IIndex caseIndex = Assert.Single(
            model.FindEntityType(typeof(RefundLedger))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_refund_ledgers_refund_case_id");
        Assert.True(caseIndex.IsUnique);

        IIndex familyIndex = Assert.Single(
            model.FindEntityType(typeof(CreditBalance))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_credit_balances_family_id");
        Assert.True(familyIndex.IsUnique);
    }

    [Fact]
    public void Model_snapshot_describes_the_current_model()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        string[] currentTables = TablesOf(context.GetService<IDesignTimeModel>().Model);
        string[] snapshotTables = TablesOf(migrations.ModelSnapshot!.Model);

        Assert.Contains("refund_ledgers", currentTables);
        Assert.Contains("refund_ledger_entries", currentTables);
        Assert.Contains("credit_balances", currentTables);
        Assert.Equal(currentTables, snapshotTables);
    }

    [Fact]
    public void Authored_migration_creates_the_three_money_core_tables()
    {
        using FinanceDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        Assert.Contains(MigrationId, migrations.Migrations.Keys);
        Type migrationType = migrations.Migrations[MigrationId].AsType();
        Assert.Equal("CreateFinanceMoneyCore", migrationType.Name);
        Assert.Equal(MigrationId, migrationType.GetCustomAttribute<MigrationAttribute>()!.Id);

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        IReadOnlyList<MigrationOperation> up = migration.UpOperations;

        Assert.Equal(
            ["finance.credit_balances", "finance.refund_ledger_entries", "finance.refund_ledgers"],
            up.OfType<CreateTableOperation>()
                .Select(operation => $"{operation.Schema}.{operation.Name}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        CreateTableOperation entries = up.OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "refund_ledger_entries");
        string[] entryColumns = entries.Columns.Select(column => column.Name).ToArray();
        foreach (string expected in new[]
        {
            "id",
            "refund_case_id",
            "entry_type",
            "amount",
            "occurred_on_utc",
            "actor_user_id",
            "evidence_reference",
            "provider_reference"
        })
        {
            Assert.Contains(expected, entryColumns);
        }

        // No applied-history seed, no data mutation: file-only migration.
        Assert.Empty(up.OfType<InsertDataOperation>());
    }

    [Fact]
    public void Authored_migration_down_drops_the_money_core_tables()
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
            .UseNpgsql("Host=localhost;Port=1;Database=w2f0_model_only;Username=unused;Password=unused")
            .Options);
}
