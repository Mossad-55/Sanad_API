using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCancellationFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_cancellation_facts",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_side = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    status_at_cancellation = table.Column<int>(type: "integer", nullable: false),
                    cancelled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_on_utc_used = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    reason_category = table.Column<int>(type: "integer", nullable: true),
                    reason_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    refund_entitlement = table.Column<int>(type: "integer", nullable: false),
                    refund_decision_reason = table.Column<int>(type: "integer", nullable: false),
                    is_caregiver_incident = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_cancellation_facts", x => x.id);
                    table.ForeignKey(
                        name: "FK_booking_cancellation_facts_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "families",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_booking_cancellation_facts_booking",
                schema: "families",
                table: "booking_cancellation_facts",
                column: "booking_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_cancellation_facts",
                schema: "families");
        }
    }
}
