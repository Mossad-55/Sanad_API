using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingsAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_type = table.Column<int>(type: "integer", nullable: false),
                    shift_type = table.Column<int>(type: "integer", nullable: false),
                    booking_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    service_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    special_instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    price_base_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price_platform_fee_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    price_platform_fee_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price_total_payable_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paymob_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paymob_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    caregiver_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paid_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirmed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_caregiver_id",
                schema: "families",
                table: "bookings",
                column: "caregiver_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_elderly_id",
                schema: "families",
                table: "bookings",
                column: "elderly_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_family_id",
                schema: "families",
                table: "bookings",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_status",
                schema: "families",
                table: "bookings",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings",
                schema: "families");
        }
    }
}
