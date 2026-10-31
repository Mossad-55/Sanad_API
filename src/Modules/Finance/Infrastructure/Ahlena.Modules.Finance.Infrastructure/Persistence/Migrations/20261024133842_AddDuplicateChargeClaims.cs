using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateChargeClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duplicate_charge_claims",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    award_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    customer_reported_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    execution_state = table.Column<int>(type: "integer", nullable: false),
                    decided_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_by_account_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decided_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    family_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    verified_basis = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    execution_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    executed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution_started_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    execution_award_minor_units = table.Column<long>(type: "bigint", nullable: true),
                    gateway_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_refund_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    not_moved_provider_evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    completed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    execution_attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_charge_claims", x => x.id);
                    table.CheckConstraint("ck_duplicate_charge_claims_attempt_count", "execution_attempt_count >= 0");
                    table.CheckConstraint("ck_duplicate_charge_claims_award", "award_minor_units > 0");
                    table.CheckConstraint("ck_duplicate_charge_claims_execution_state", "execution_state IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("ck_duplicate_charge_claims_state", "state IN (1, 2, 3)");
                    table.CheckConstraint("ck_duplicate_charge_claims_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_charge_claims_payment_attempt_id",
                schema: "finance",
                table: "duplicate_charge_claims",
                column: "payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_charge_claims_refund_case_id",
                schema: "finance",
                table: "duplicate_charge_claims",
                column: "refund_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_charge_claims_state",
                schema: "finance",
                table: "duplicate_charge_claims",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ux_duplicate_charge_claims_family_attempt_active",
                schema: "finance",
                table: "duplicate_charge_claims",
                columns: new[] { "family_account_id", "payment_attempt_id" },
                unique: true,
                filter: "\"state\" <> 2");

            migrationBuilder.CreateIndex(
                name: "ux_duplicate_charge_claims_family_idempotency_key",
                schema: "finance",
                table: "duplicate_charge_claims",
                columns: new[] { "family_account_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duplicate_charge_claims",
                schema: "finance");
        }
    }
}
