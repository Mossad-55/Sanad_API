using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionRenewalLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_renew_enabled",
                schema: "families",
                table: "family_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancellation_requested_on_utc",
                schema: "families",
                table: "family_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "current_period_ends_on_utc",
                schema: "families",
                table: "family_subscriptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "lifecycle_version",
                schema: "families",
                table: "family_subscriptions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM families.family_subscriptions
                        WHERE cycle NOT IN (1, 2)) THEN
                        RAISE EXCEPTION 'Unsupported subscription cycle in family_subscriptions.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                UPDATE families.family_subscriptions
                SET current_period_ends_on_utc = created_on_utc +
                    CASE cycle
                        WHEN 1 THEN INTERVAL '1 month'
                        WHEN 2 THEN INTERVAL '1 year'
                    END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE families.family_subscriptions
                ALTER COLUMN current_period_ends_on_utc DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_renew_enabled",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "cancellation_requested_on_utc",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_period_ends_on_utc",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "lifecycle_version",
                schema: "families",
                table: "family_subscriptions");
        }
    }
}
