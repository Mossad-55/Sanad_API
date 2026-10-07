using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeRefundLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "refund_amount",
                schema: "care_homes",
                table: "bookings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "refund_completed_by",
                schema: "care_homes",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refund_completed_on_utc",
                schema: "care_homes",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_failure_reason",
                schema: "care_homes",
                table: "bookings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refund_status",
                schema: "care_homes",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE care_homes.bookings
                SET refund_status = CASE WHEN "Status" = 8 THEN 2 WHEN "Status" = 7 THEN 1 ELSE 0 END,
                    refund_amount = CASE WHEN "Status" IN (7, 8) THEN "TotalAmount" ELSE NULL END
                WHERE "Status" IN (7, 8);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refund_amount",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_completed_by",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_completed_on_utc",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_failure_reason",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_status",
                schema: "care_homes",
                table: "bookings");
        }
    }
}
