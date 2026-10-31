using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// W2-F0 Finance money core. Creates the append-only refund ledger
    /// (case root plus entries), the per-family app credit balance, and the
    /// platform control audit table's sibling conventions. Authored by hand and
    /// deliberately not applied: the migration is a file-only deliverable and
    /// the Mastermind regenerates the Designer canonically at the gate.
    /// </summary>
    public partial class CreateFinanceMoneyCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refund_ledgers",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_ledgers", x => x.id);
                });

            // The unique principal key must exist BEFORE the dependent table's
            // FK is created: PostgreSQL validates FK targets at DDL time.
            migrationBuilder.CreateIndex(
                name: "ux_refund_ledgers_refund_case_id",
                schema: "finance",
                table: "refund_ledgers",
                column: "refund_case_id",
                unique: true);

            migrationBuilder.CreateTable(
                name: "refund_ledger_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    // The only reliable creation-order key: version-7 ids are
                    // time-ordered at millisecond precision only, so the ledger
                    // persists an explicit per-case append sequence.
                    entry_sequence = table.Column<long>(type: "bigint", nullable: false),
                    refund_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<int>(type: "integer", nullable: false),
                    // Money is stored in EGP major units as numeric(18,2), the
                    // same major-unit convention as every other money column.
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    // Null actor = the platform itself acted (for example a
                    // timeout sweep); a real user id is required otherwise.
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_refund_ledger_entries_amount_precision", "amount = round(amount, 2)");
                    table.CheckConstraint("ck_refund_ledger_entries_entry_type", "entry_type IN (1, 2, 3, 4, 5, 6, 7, 8)");
                    table.ForeignKey(
                        name: "FK_refund_ledger_entries_refund_ledgers_refund_case_id",
                        column: x => x.refund_case_id,
                        principalSchema: "finance",
                        principalTable: "refund_ledgers",
                        principalColumn: "refund_case_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "credit_balances",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    // Balance in EGP major units; the non-negative invariant is
                    // enforced by the domain operation and this check constraint.
                    balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_balances", x => x.id);
                    table.CheckConstraint("ck_credit_balances_balance_non_negative", "balance >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_refund_ledger_entries_refund_case_id",
                schema: "finance",
                table: "refund_ledger_entries",
                column: "refund_case_id");

            migrationBuilder.CreateIndex(
                name: "ux_refund_ledger_entries_case_sequence",
                schema: "finance",
                table: "refund_ledger_entries",
                columns: new[] { "refund_case_id", "entry_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refund_ledger_entries_occurred_on_utc",
                schema: "finance",
                table: "refund_ledger_entries",
                column: "occurred_on_utc");

            migrationBuilder.CreateIndex(
                name: "ux_credit_balances_family_id",
                schema: "finance",
                table: "credit_balances",
                column: "family_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_balances",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "refund_ledger_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "refund_ledgers",
                schema: "finance");
        }
    }
}
