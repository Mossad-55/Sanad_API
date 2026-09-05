using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingPaymentTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "paymob_refund_transaction_id",
                schema: "families",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunded_on_utc",
                schema: "families",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paymob_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    paymob_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    method = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    settled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refunded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transactions", x => new { x.booking_id, x.id });
                    table.ForeignKey(
                        name: "FK_payment_transactions_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "families",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_paymob_order_id",
                schema: "families",
                table: "payment_transactions",
                column: "paymob_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_paymob_transaction_id",
                schema: "families",
                table: "payment_transactions",
                column: "paymob_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_transactions",
                schema: "families");

            migrationBuilder.DropColumn(
                name: "paymob_refund_transaction_id",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refunded_on_utc",
                schema: "families",
                table: "bookings");
        }
    }
}
