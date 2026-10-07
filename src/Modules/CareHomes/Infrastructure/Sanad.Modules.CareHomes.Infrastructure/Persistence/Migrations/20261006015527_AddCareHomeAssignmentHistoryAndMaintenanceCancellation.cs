using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeAssignmentHistoryAndMaintenanceCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedOnUtc",
                schema: "care_homes",
                table: "maintenance_blocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE care_homes.maintenance_blocks SET \"UpdatedOnUtc\" = \"CreatedOnUtc\" WHERE \"UpdatedOnUtc\" IS NULL;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedOnUtc",
                schema: "care_homes",
                table: "maintenance_blocks",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by",
                schema: "care_homes",
                table: "maintenance_blocks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_on_utc",
                schema: "care_homes",
                table: "maintenance_blocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "booking_assignment_history",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_bed_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_bed_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_assignment_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_notification_outbox",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_attempt_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_notification_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_booking_assignment_history_booking_id_effective_date",
                schema: "care_homes",
                table: "booking_assignment_history",
                columns: new[] { "booking_id", "effective_date" },
                unique: true,
                filter: "\"from_room_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transfer_notification_outbox_Status_next_attempt_on_utc",
                schema: "care_homes",
                table: "transfer_notification_outbox",
                columns: new[] { "Status", "next_attempt_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_transfer_notification_outbox_transfer_id",
                schema: "care_homes",
                table: "transfer_notification_outbox",
                column: "transfer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_assignment_history",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "transfer_notification_outbox",
                schema: "care_homes");

            migrationBuilder.DropColumn(
                name: "UpdatedOnUtc",
                schema: "care_homes",
                table: "maintenance_blocks");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                schema: "care_homes",
                table: "maintenance_blocks");

            migrationBuilder.DropColumn(
                name: "cancelled_on_utc",
                schema: "care_homes",
                table: "maintenance_blocks");
        }
    }
}
