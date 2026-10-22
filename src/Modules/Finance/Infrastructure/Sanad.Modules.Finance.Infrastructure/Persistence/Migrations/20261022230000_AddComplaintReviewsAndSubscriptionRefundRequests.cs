using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// W3-F3 additive Finance migration owned by the F3 lane. Adds the reviewed
    /// complaint-review case table (clause 5, Owner decisions #11-#14), the M7
    /// idempotency marker table for subscription refund requests (design pin item
    /// 4), and the per-capture refundable budget that makes Owner decisions #4 and
    /// #6 ("never refund more than captured funds less previous refunds"; the
    /// shutdown award is the unused portion of <em>that</em> paid period)
    /// enforceable across the different clause-9 refund cases. Authored by hand
    /// and deliberately NOT applied: the migration is a file-only deliverable and
    /// its predecessor <c>20261022210000_CreateFinanceMoneyCore</c> is itself
    /// files-only.
    /// </summary>
    public partial class AddComplaintReviewsAndSubscriptionRefundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "complaint_reviews",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporting_party = table.Column<int>(type: "integer", nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    visited_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    timeliness = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    investigator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    investigation_started_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refused_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    refused_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complaint_reviews", x => x.id);
                    table.CheckConstraint("ck_complaint_reviews_reporting_party", "reporting_party IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_complaint_reviews_state", "state IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_complaint_reviews_timeliness", "timeliness IN (1, 2, 3)");
                    table.CheckConstraint(
                        "ck_complaint_reviews_resolution_attributable",
                        "(state <> 3 OR (approved_by_user_id IS NOT NULL AND approved_on_utc IS NOT NULL)) AND " +
                        "(state <> 4 OR (refused_by_user_id IS NOT NULL AND refused_on_utc IS NOT NULL))");
                    table.CheckConstraint(
                        "ck_complaint_reviews_single_verdict",
                        "NOT (approved_on_utc IS NOT NULL AND refused_on_utc IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "subscription_refund_requests",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_refund_requests", x => x.id);
                    table.CheckConstraint("ck_subscription_refund_requests_reason", "reason IN (1, 2, 3)");
                    table.CheckConstraint("ck_subscription_refund_requests_amount", "requested_amount_minor_units >= 0");
                    table.CheckConstraint(
                        "ck_subscription_refund_requests_key_length",
                        "char_length(idempotency_key) BETWEEN 1 AND 200");
                });

            // The shared serialization point for every clause-9 refund case that
            // draws on one CAPTURE (Owner decisions #4 and #6). A duplicate-charge
            // case targets the duplicated attempt; a shutdown case targets the
            // attempt that paid for the current period. Both resolve to this
            // budget, so their awards cannot refund the same captured money twice
            // in either order — and because the key is the capture rather than the
            // subscription, an older period's consumed money never supplies
            // headroom for the current one.
            migrationBuilder.CreateTable(
                name: "subscription_refund_budgets",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    claimed_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_refund_budgets", x => x.id);
                    table.CheckConstraint("ck_subscription_refund_budgets_captured", "captured_minor_units >= 0");
                    table.CheckConstraint("ck_subscription_refund_budgets_claimed", "claimed_minor_units >= 0");
                    table.CheckConstraint(
                        "ck_subscription_refund_budgets_within_captured",
                        "claimed_minor_units <= captured_minor_units");
                    table.CheckConstraint("ck_subscription_refund_budgets_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_complaint_reviews_refund_case_id",
                schema: "finance",
                table: "complaint_reviews",
                column: "refund_case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_complaint_reviews_state_reported_on_utc",
                schema: "finance",
                table: "complaint_reviews",
                columns: new[] { "state", "reported_on_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_complaint_reviews_family_reported_on_utc",
                schema: "finance",
                table: "complaint_reviews",
                columns: new[] { "family_id", "reported_on_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_subscription_refund_requests_family_idempotency_key",
                schema: "finance",
                table: "subscription_refund_requests",
                columns: new[] { "family_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_subscription_refund_requests_refund_case_id",
                schema: "finance",
                table: "subscription_refund_requests",
                column: "refund_case_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscription_refund_requests_subscription_id",
                schema: "finance",
                table: "subscription_refund_requests",
                column: "subscription_id");

            // One budget per CAPTURE: the unique row every clause-9 case against
            // that capture serializes on.
            migrationBuilder.CreateIndex(
                name: "ux_subscription_refund_budgets_payment_attempt_id",
                schema: "finance",
                table: "subscription_refund_budgets",
                column: "payment_attempt_id",
                unique: true);

            // Audit correlation only: a subscription owns many captures, so this is
            // deliberately not unique.
            migrationBuilder.CreateIndex(
                name: "ix_subscription_refund_budgets_subscription_id",
                schema: "finance",
                table: "subscription_refund_budgets",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_refund_budgets_family_id",
                schema: "finance",
                table: "subscription_refund_budgets",
                column: "family_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "complaint_reviews",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "subscription_refund_budgets",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "subscription_refund_requests",
                schema: "finance");
        }
    }
}
