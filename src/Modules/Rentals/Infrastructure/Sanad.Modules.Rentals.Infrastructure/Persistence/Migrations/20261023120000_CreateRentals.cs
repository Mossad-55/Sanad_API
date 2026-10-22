using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Rentals.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// W3-F4 additive Rentals migration owned by the F4 lane. Creates the
    /// clause 8 lifecycle tables (catalog, bookings, payment transactions,
    /// damage deductions, refund movements) in the module's own
    /// <c>rentals</c> migration slot (Owner decision #33). Money columns are
    /// the standardized EGP major-unit <c>numeric(18,2)</c> convention
    /// (Owner decision #37). Authored by hand and deliberately NOT applied:
    /// the migration is a file-only deliverable.
    /// </summary>
    public partial class CreateRentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rentals");

            migrationBuilder.CreateTable(
                name: "rental_equipment",
                schema: "rentals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_english = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_arabic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_english = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    description_arabic = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    daily_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    maximum_rental_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_equipment", x => x.id);
                    table.CheckConstraint("ck_rental_equipment_daily_rate_positive", "daily_rate > 0");
                    table.CheckConstraint("ck_rental_equipment_deposit_non_negative", "deposit_amount >= 0");
                    table.CheckConstraint("ck_rental_equipment_maximum_rental_days", "maximum_rental_days >= 1 AND maximum_rental_days <= 365");
                });

            migrationBuilder.CreateTable(
                name: "rental_bookings",
                schema: "rentals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_days = table.Column<int>(type: "integer", nullable: false),
                    daily_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    maximum_rental_days_snapshot = table.Column<int>(type: "integer", nullable: false),
                    name_english_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_arabic_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    delivery_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    pending_refund_kind = table.Column<int>(type: "integer", nullable: true),
                    pending_refund_case_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_refund_destination = table.Column<int>(type: "integer", nullable: true),
                    pending_refund_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    terms_snapshot_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    captured_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    delivered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    returned_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    settlement_deadline_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspection_completed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    inspection_completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inspection_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    cancelled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deposit_settled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deposit_settled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_bookings", x => x.id);
                    table.CheckConstraint("ck_rental_bookings_captured_total_non_negative", "captured_total IS NULL OR captured_total >= 0");
                    table.CheckConstraint("ck_rental_bookings_daily_rate_positive", "daily_rate > 0");
                    table.CheckConstraint("ck_rental_bookings_deposit_non_negative", "deposit_amount >= 0");
                    table.CheckConstraint("ck_rental_bookings_pending_refund_amount_non_negative", "pending_refund_amount IS NULL OR pending_refund_amount >= 0");
                    table.CheckConstraint("ck_rental_bookings_pending_refund_kind", "pending_refund_kind IS NULL OR pending_refund_kind IN (1, 2, 3)");
                    table.CheckConstraint("ck_rental_bookings_pending_refund_destination", "pending_refund_destination IS NULL OR pending_refund_destination IN (1, 2)");
                    table.CheckConstraint("ck_rental_bookings_planned_days", "planned_days >= 1");
                    table.CheckConstraint("ck_rental_bookings_status", "status IN (1, 2, 3, 4, 5, 6)");
                });

            migrationBuilder.CreateTable(
                name: "rental_payment_transactions",
                schema: "rentals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paymob_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    paymob_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    method = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    settled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_payment_transactions", x => x.id);
                    table.CheckConstraint("ck_rental_payment_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_rental_payment_transactions_method", "method IN (1, 2)");
                    table.CheckConstraint("ck_rental_payment_transactions_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_rental_payment_transactions_rental_bookings_rental_booking_id",
                        column: x => x.rental_booking_id,
                        principalSchema: "rentals",
                        principalTable: "rental_bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rental_damage_deductions",
                schema: "rentals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description_english = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    description_arabic = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    recorded_by_admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dispute_state = table.Column<int>(type: "integer", nullable: false),
                    disputed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dispute_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    dispute_resolved_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dispute_resolved_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_damage_deductions", x => x.id);
                    table.CheckConstraint("ck_rental_damage_deductions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_rental_damage_deductions_dispute_state", "dispute_state IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_rental_damage_deductions_rental_bookings_rental_booking_id",
                        column: x => x.rental_booking_id,
                        principalSchema: "rentals",
                        principalTable: "rental_bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rental_refund_movements",
                schema: "rentals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rental_booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    refund_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_refund_movements", x => x.id);
                    table.CheckConstraint("ck_rental_refund_movements_amount_non_negative", "amount >= 0");
                    table.CheckConstraint("ck_rental_refund_movements_destination", "destination IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_rental_refund_movements_kind", "kind IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_rental_refund_movements_rental_bookings_rental_booking_id",
                        column: x => x.rental_booking_id,
                        principalSchema: "rentals",
                        principalTable: "rental_bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rental_bookings_family_id",
                schema: "rentals",
                table: "rental_bookings",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_rental_bookings_status",
                schema: "rentals",
                table: "rental_bookings",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_rental_bookings_pending_refund_case_id",
                schema: "rentals",
                table: "rental_bookings",
                column: "pending_refund_case_id",
                unique: true,
                filter: "pending_refund_case_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rental_payment_transactions_paymob_order_id",
                schema: "rentals",
                table: "rental_payment_transactions",
                column: "paymob_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rental_payment_transactions_rental_booking_id",
                schema: "rentals",
                table: "rental_payment_transactions",
                column: "rental_booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_rental_damage_deductions_rental_booking_id",
                schema: "rentals",
                table: "rental_damage_deductions",
                column: "rental_booking_id");

            migrationBuilder.CreateIndex(
                name: "ux_rental_refund_movements_booking_kind",
                schema: "rentals",
                table: "rental_refund_movements",
                columns: new[] { "rental_booking_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rental_refund_movements_refund_case_id",
                schema: "rentals",
                table: "rental_refund_movements",
                column: "refund_case_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rental_damage_deductions",
                schema: "rentals");

            migrationBuilder.DropTable(
                name: "rental_payment_transactions",
                schema: "rentals");

            migrationBuilder.DropTable(
                name: "rental_refund_movements",
                schema: "rentals");

            migrationBuilder.DropTable(
                name: "rental_bookings",
                schema: "rentals");

            migrationBuilder.DropTable(
                name: "rental_equipment",
                schema: "rentals");
        }
    }
}
