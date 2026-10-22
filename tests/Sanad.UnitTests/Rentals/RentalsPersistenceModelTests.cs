using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sanad.Modules.Rentals.Domain;
using Sanad.Modules.Rentals.Infrastructure;

namespace Sanad.UnitTests.Rentals;

/// <summary>
/// W3-F4 persistence model and migration. The migration is authored and
/// deliberately NOT applied: these assertions read the compiled migration
/// operations and the design-time model without connecting to any database.
/// Run green at the W3 gate by the Mastermind.
/// </summary>
public sealed class RentalsPersistenceModelTests
{
    private const string MigrationId = "20261023120000_CreateRentals";

    [Fact]
    public void Money_columns_use_the_standardized_numeric_18_2_convention()
    {
        using RentalsDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType booking = model.FindEntityType(typeof(RentalBooking))!;
        foreach (string moneyProperty in new[] { nameof(RentalBooking.DailyRateMinorUnits), nameof(RentalBooking.DepositMinorUnits), nameof(RentalBooking.CapturedMinorUnits) })
        {
            IProperty property = booking.FindProperty(moneyProperty)!;
            Assert.Equal("numeric(18,2)", property.GetColumnType());
            Assert.Equal(18, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
        }

        IEntityType equipment = model.FindEntityType(typeof(RentalEquipment))!;
        Assert.Equal("numeric(18,2)", equipment.FindProperty(nameof(RentalEquipment.DailyRateMinorUnits))!.GetColumnType());
        Assert.Equal("numeric(18,2)", equipment.FindProperty(nameof(RentalEquipment.DepositMinorUnits))!.GetColumnType());

        IEntityType transaction = model.FindEntityType(typeof(RentalPaymentTransaction))!;
        Assert.Equal("numeric(18,2)", transaction.FindProperty(nameof(RentalPaymentTransaction.AmountMinorUnits))!.GetColumnType());

        IEntityType deduction = model.FindEntityType(typeof(RentalDamageDeduction))!;
        Assert.Equal("numeric(18,2)", deduction.FindProperty(nameof(RentalDamageDeduction.AmountMinorUnits))!.GetColumnType());

        IEntityType movement = model.FindEntityType(typeof(RentalRefundMovement))!;
        Assert.Equal("numeric(18,2)", movement.FindProperty(nameof(RentalRefundMovement.AmountMinorUnits))!.GetColumnType());
    }

    [Fact]
    public void The_booking_version_is_an_optimistic_concurrency_token()
    {
        // The refund-claim race guard: two concurrent BeginRefund saves cannot
        // both land - the loser is a retryable conflict, never a second money
        // movement.
        using RentalsDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IProperty version = model
            .FindEntityType(typeof(RentalBooking))!
            .FindProperty(nameof(RentalBooking.Version))!;
        Assert.True(version.IsConcurrencyToken);
    }

    [Fact]
    public void Rentals_tables_map_into_the_rentals_schema()
    {
        using RentalsDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal("rentals", RentalsDbContext.Schema);

        (Type EntityType, string Table, string IdColumn)[] expected =
        [
            (typeof(RentalEquipment), "rental_equipment", "id"),
            (typeof(RentalBooking), "rental_bookings", "id"),
            (typeof(RentalPaymentTransaction), "rental_payment_transactions", "id"),
            (typeof(RentalDamageDeduction), "rental_damage_deductions", "id"),
            (typeof(RentalRefundMovement), "rental_refund_movements", "id")
        ];

        foreach ((Type entityType, string table, string idColumn) in expected)
        {
            IEntityType entity = model.FindEntityType(entityType)!;
            Assert.Equal("rentals", entity.GetSchema());
            Assert.Equal(table, entity.GetTableName());
            Assert.Equal(idColumn, entity.FindProperty(nameof(RentalBooking.Id))!.GetColumnName());
        }

        // The pending refund claim is persisted on the booking itself (design
        // pin item 3): the correlation id lives on the owner's rows first.
        IEntityType booking = model.FindEntityType(typeof(RentalBooking))!;
        Assert.Equal("pending_refund_case_id", booking.FindProperty(nameof(RentalBooking.PendingRefundCaseId))!.GetColumnName());
        Assert.Equal("pending_refund_kind", booking.FindProperty(nameof(RentalBooking.PendingRefundKind))!.GetColumnName());
        Assert.Equal("pending_refund_amount", booking.FindProperty(nameof(RentalBooking.PendingRefundAmountMinorUnits))!.GetColumnName());
    }

    [Fact]
    public void Rental_idempotency_boundaries_are_unique_indexes()
    {
        using RentalsDbContext context = CreateNpgsqlContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        // One refund movement of a kind per booking, ever.
        IIndex movementIndex = Assert.Single(
            model.FindEntityType(typeof(RentalRefundMovement))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_rental_refund_movements_booking_kind");
        Assert.True(movementIndex.IsUnique);

        // One pending refund claim at a time.
        IIndex pendingIndex = Assert.Single(
            model.FindEntityType(typeof(RentalBooking))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_rental_bookings_pending_refund_case_id");
        Assert.True(pendingIndex.IsUnique);

        // The webhook's merchant_order_id lookup rides the intent key.
        IIndex orderIndex = Assert.Single(
            model.FindEntityType(typeof(RentalPaymentTransaction))!.GetIndexes(),
            index => index.GetDatabaseName() == "ux_rental_payment_transactions_paymob_order_id");
        Assert.True(orderIndex.IsUnique);
    }

    [Fact]
    public void Model_snapshot_describes_the_current_model()
    {
        using RentalsDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        string[] currentTables = TablesOf(context.GetService<IDesignTimeModel>().Model);
        string[] snapshotTables = TablesOf(migrations.ModelSnapshot!.Model);

        Assert.Contains("rental_equipment", currentTables);
        Assert.Contains("rental_bookings", currentTables);
        Assert.Contains("rental_payment_transactions", currentTables);
        Assert.Contains("rental_damage_deductions", currentTables);
        Assert.Contains("rental_refund_movements", currentTables);
        Assert.Equal(currentTables, snapshotTables);
    }

    [Fact]
    public void Authored_migration_creates_the_five_rentals_tables()
    {
        using RentalsDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();

        Assert.Contains(MigrationId, migrations.Migrations.Keys);
        Type migrationType = migrations.Migrations[MigrationId].AsType();
        Assert.Equal("CreateRentals", migrationType.Name);
        Assert.Equal(MigrationId, migrationType.GetCustomAttribute<MigrationAttribute>()!.Id);

        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        IReadOnlyList<MigrationOperation> up = migration.UpOperations;

        Assert.Equal(
            new[]
            {
                "rentals.rental_bookings",
                "rentals.rental_damage_deductions",
                "rentals.rental_equipment",
                "rentals.rental_payment_transactions",
                "rentals.rental_refund_movements"
            },
            up.OfType<CreateTableOperation>()
                .Select(operation => $"{operation.Schema}.{operation.Name}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        CreateTableOperation movements = up.OfType<CreateTableOperation>()
            .Single(operation => operation.Name == "rental_refund_movements");
        string[] movementColumns = movements.Columns.Select(column => column.Name).ToArray();
        foreach (string expected in new[]
        {
            "id",
            "rental_booking_id",
            "kind",
            "refund_case_id",
            "destination",
            "amount",
            "actor_user_id",
            "gateway_reference",
            "evidence_reference",
            "recorded_on_utc"
        })
        {
            Assert.Contains(expected, movementColumns);
        }

        // Money columns are numeric(18,2) in the migration itself (#37).
        ColumnOperation amountColumn = Assert.Single(
            movements.Columns,
            column => column.Name == "amount");
        Assert.Equal("numeric(18,2)", amountColumn.ColumnType);

        // No applied-history seed, no data mutation: file-only migration.
        Assert.Empty(up.OfType<InsertDataOperation>());
    }

    [Fact]
    public void Authored_migration_down_drops_the_rentals_tables()
    {
        using RentalsDbContext context = CreateNpgsqlContext();
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        var migration = (Migration)Activator.CreateInstance(migrations.Migrations[MigrationId].AsType())!;

        Assert.Equal(5, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    private static string[] TablesOf(IModel model) => model.GetEntityTypes()
        .Select(entity => entity.GetTableName())
        .Where(name => name is not null)
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static RentalsDbContext CreateNpgsqlContext() => new(
        new DbContextOptionsBuilder<RentalsDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=w3f4_model_only;Username=unused;Password=unused")
            .Options);
}
