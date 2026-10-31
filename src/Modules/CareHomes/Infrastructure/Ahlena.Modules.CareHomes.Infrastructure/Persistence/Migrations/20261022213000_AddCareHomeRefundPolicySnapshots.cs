using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeRefundPolicySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_terms_snapshot_json",
                schema: "care_homes",
                table: "bookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "refund_policy_document_effective_on",
                schema: "care_homes",
                table: "bookings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "refund_policy_version",
                schema: "care_homes",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "is_refund_outcome_ambiguous",
                schema: "care_homes",
                table: "bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "refund_ambiguity_award_amount",
                schema: "care_homes",
                table: "bookings",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_ambiguity_cleared_by",
                schema: "care_homes",
                table: "bookings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refund_ambiguity_cleared_on_utc",
                schema: "care_homes",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refund_ambiguity_raised_on_utc",
                schema: "care_homes",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_ambiguity_reason",
                schema: "care_homes",
                table: "bookings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "refund_attempt_claim_id",
                schema: "care_homes",
                table: "bookings",
                type: "uuid",
                nullable: true);

            // Backfill: a row claimed by a pre-deployment attempt arrived with
            // no attempt identity and could never satisfy the reconcile
            // compare-and-swap. Exactly the rows carrying a live claim get a
            // stable generated uuid (PG13+ built-in); the marker rows among
            // them keep their semantics — the claim and its identity fold
            // into the ambiguity marker and resolve together at
            // reconciliation. Data-only step: no model change, so the
            // Designer and model snapshot stay untouched. No other backfill
            // is implied: is_refund_outcome_ambiguous arrived with its
            // house-style false default, the remaining ambiguity fields are
            // meaningful as NULL for pre-lane rows, and the legacy policy
            // defaults already stand from this migration's earlier columns.
            migrationBuilder.Sql(
                "UPDATE care_homes.bookings SET refund_attempt_claim_id = gen_random_uuid() WHERE refund_claimed_on_utc IS NOT NULL AND refund_attempt_claim_id IS NULL;");

            migrationBuilder.AddColumn<string>(
                name: "cancellation_terms_json",
                schema: "care_homes",
                table: "profile_revisions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_terms_json",
                schema: "care_homes",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "cancellation_terms_snapshot_json",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_policy_document_effective_on",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_policy_version",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "is_refund_outcome_ambiguous",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_award_amount",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_cleared_by",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_cleared_on_utc",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_raised_on_utc",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_reason",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_attempt_claim_id",
                schema: "care_homes",
                table: "bookings");
            // The drop discards attempt identities, including any rows the
            // data-only Up backfill assigned; a backfill of generated values
            // has no faithful down counterpart by design.
        }
    }
}
