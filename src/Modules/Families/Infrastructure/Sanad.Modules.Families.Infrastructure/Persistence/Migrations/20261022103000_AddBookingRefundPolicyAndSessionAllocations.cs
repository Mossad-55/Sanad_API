using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingRefundPolicyAndSessionAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "refund_policy_document_effective_on",
                schema: "families",
                table: "bookings",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "refund_policy_version",
                schema: "families",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "refunded_minor_units",
                schema: "families",
                table: "bookings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Decision #17 ambiguity marker: an unconfirmed provider refund outcome is recorded
            // here and blocks further attempts until an operator reconciles it.
            migrationBuilder.AddColumn<long>(
                name: "refund_ambiguity_award_minor_units",
                schema: "families",
                table: "bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_ambiguity_cleared_by",
                schema: "families",
                table: "bookings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refund_ambiguity_cleared_on_utc",
                schema: "families",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refund_ambiguity_raised_on_utc",
                schema: "families",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_ambiguity_reason",
                schema: "families",
                table: "bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_ambiguity_reference",
                schema: "families",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_refund_outcome_ambiguous",
                schema: "families",
                table: "bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Revision-2 money evidence on the cancellation fact: the award, and the full-refund
            // basis for a partial award. Nullable — a revision-1 fact is amount-free.
            migrationBuilder.AddColumn<long>(
                name: "awarded_minor_units",
                schema: "families",
                table: "booking_cancellation_facts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "partial_entitlement_minor_units",
                schema: "families",
                table: "booking_cancellation_facts",
                type: "bigint",
                nullable: true);

            // Refund-attempt claim: committed before any external refund call so an interrupted
            // attempt is durable evidence. refund_attempt_version doubles as the optimistic
            // concurrency token that makes the claim exclusive.
            migrationBuilder.AddColumn<int>(
                name: "refund_attempt_version",
                schema: "families",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "refund_attempt_claim_id",
                schema: "families",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refund_attempt_claimed_on_utc",
                schema: "families",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "refund_attempt_award_minor_units",
                schema: "families",
                table: "bookings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_refund_attempt_in_flight",
                schema: "families",
                table: "bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "booking_session_allocations",
                schema: "families",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_index = table.Column<int>(type: "integer", nullable: false),
                    allocated_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    delivered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "pk_booking_session_allocations",
                        x => new { x.booking_id, x.session_index });
                    table.CheckConstraint(
                        "ck_booking_session_allocations_allocated_non_negative",
                        "allocated_minor_units >= 0");
                    table.CheckConstraint(
                        "ck_booking_session_allocations_outcome_range",
                        "outcome BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_booking_session_allocations_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "families",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_session_allocations",
                schema: "families");

            migrationBuilder.DropColumn(
                name: "refund_policy_document_effective_on",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_policy_version",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refunded_minor_units",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_award_minor_units",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_cleared_by",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_cleared_on_utc",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_raised_on_utc",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_reason",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_ambiguity_reference",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "is_refund_outcome_ambiguous",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "awarded_minor_units",
                schema: "families",
                table: "booking_cancellation_facts");

            migrationBuilder.DropColumn(
                name: "partial_entitlement_minor_units",
                schema: "families",
                table: "booking_cancellation_facts");

            migrationBuilder.DropColumn(
                name: "refund_attempt_version",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_attempt_claim_id",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_attempt_claimed_on_utc",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refund_attempt_award_minor_units",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "is_refund_attempt_in_flight",
                schema: "families",
                table: "bookings");
        }
    }
}
