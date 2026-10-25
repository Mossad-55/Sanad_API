using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using CareHomesMigration =
    Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations.AddIdempotencyRequests;
using FamiliesMigration =
    Sanad.Modules.Families.Infrastructure.Persistence.Migrations.AddIdempotencyRequests;

namespace Sanad.UnitTests.Persistence;

/// <summary>
/// Schema assertions for the two unapplied idempotency migrations. Each writing
/// context owns one table in its own schema, and the unique index on
/// <c>(actor_id, operation, idempotency_key)</c> is what makes concurrent
/// duplicates safe.
/// </summary>
public sealed class IdempotencyMigrationTests
{
    [Fact]
    public void FamiliesMigration_CreatesTheFamilySchemaTableAndIndexes()
    {
        MigrationBuilder builder = BuildUp<FamiliesMigration>();

        CreateTableOperation table = AssertSingleTable(builder, "families");
        AssertColumns(table);
        AssertIndexes(builder, "families");

        DropTableOperation down = AssertSingleDrop(BuildDown<FamiliesMigration>());
        Assert.Equal("families", down.Schema);
        Assert.Equal("idempotency_requests", down.Name);
    }

    [Fact]
    public void CareHomesMigration_CreatesTheCareHomesSchemaTableAndIndexes()
    {
        MigrationBuilder builder = BuildUp<CareHomesMigration>();

        CreateTableOperation table = AssertSingleTable(builder, "care_homes");
        AssertColumns(table);
        AssertIndexes(builder, "care_homes");

        DropTableOperation down = AssertSingleDrop(BuildDown<CareHomesMigration>());
        Assert.Equal("care_homes", down.Schema);
        Assert.Equal("idempotency_requests", down.Name);
    }

    private static CreateTableOperation AssertSingleTable(
        MigrationBuilder builder,
        string schema)
    {
        CreateTableOperation table = Assert.Single(
            builder.Operations.OfType<CreateTableOperation>());

        Assert.Equal("idempotency_requests", table.Name);
        Assert.Equal(schema, table.Schema);
        Assert.NotNull(table.PrimaryKey);
        Assert.Equal(["id"], table.PrimaryKey!.Columns);

        return table;
    }

    private static void AssertColumns(CreateTableOperation table)
    {
        Assert.Equal(
            [
                "id",
                "actor_id",
                "operation",
                "idempotency_key",
                "fingerprint",
                "resource_id",
                "response_status",
                "response_body",
                "created_on_utc"
            ],
            table.Columns.Select(column => column.Name));

        Assert.Equal(
            "character varying(64)",
            Column(table, "operation").ColumnType);
        Assert.Equal(64, Column(table, "operation").MaxLength);
        Assert.Equal(
            "character varying(128)",
            Column(table, "idempotency_key").ColumnType);
        Assert.Equal(128, Column(table, "idempotency_key").MaxLength);
        Assert.Equal("char(64)", Column(table, "fingerprint").ColumnType);
        Assert.Equal(64, Column(table, "fingerprint").MaxLength);
        Assert.Equal("text", Column(table, "response_body").ColumnType);

        Assert.True(Column(table, "resource_id").IsNullable);
        Assert.False(Column(table, "id").IsNullable);
        Assert.False(Column(table, "actor_id").IsNullable);
        Assert.False(Column(table, "operation").IsNullable);
        Assert.False(Column(table, "idempotency_key").IsNullable);
        Assert.False(Column(table, "fingerprint").IsNullable);
        Assert.False(Column(table, "response_status").IsNullable);
        Assert.False(Column(table, "response_body").IsNullable);
        Assert.False(Column(table, "created_on_utc").IsNullable);
    }

    private static void AssertIndexes(MigrationBuilder builder, string schema)
    {
        CreateIndexOperation[] indexes =
            builder.Operations.OfType<CreateIndexOperation>().ToArray();

        CreateIndexOperation unique = Assert.Single(
            indexes,
            index => index.IsUnique);
        Assert.Equal(
            "IX_idempotency_requests_actor_id_operation_idempotency_key",
            unique.Name);
        Assert.Equal(schema, unique.Schema);
        Assert.Equal("idempotency_requests", unique.Table);
        Assert.Equal(
            ["actor_id", "operation", "idempotency_key"],
            unique.Columns);

        CreateIndexOperation createdOn = Assert.Single(
            indexes,
            index => !index.IsUnique);
        Assert.Equal(
            "IX_idempotency_requests_created_on_utc",
            createdOn.Name);
        Assert.Equal(schema, createdOn.Schema);
        Assert.Equal("idempotency_requests", createdOn.Table);
        Assert.Equal(["created_on_utc"], createdOn.Columns);
    }

    private static AddColumnOperation Column(
        CreateTableOperation table,
        string name) =>
        table.Columns.Single(column => column.Name == name);

    private static DropTableOperation AssertSingleDrop(MigrationBuilder builder) =>
        Assert.Single(builder.Operations.OfType<DropTableOperation>());

    private static MigrationBuilder BuildUp<TMigration>()
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        Invoke<TMigration>("Up", builder);
        return builder;
    }

    private static MigrationBuilder BuildDown<TMigration>()
        where TMigration : Migration, new()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        Invoke<TMigration>("Down", builder);
        return builder;
    }

    private static void Invoke<TMigration>(string method, MigrationBuilder builder)
        where TMigration : Migration, new()
    {
        var migration = new TMigration();

        MethodInfo upOrDown = typeof(TMigration).GetMethod(
            method,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"{typeof(TMigration).Name} has no {method} method.");

        upOrDown.Invoke(migration, [builder]);
    }
}
