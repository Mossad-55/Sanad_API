using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeFinanceReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "payment_completed_on_utc",
                schema: "care_homes",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "internal_booking_notes",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_internal_booking_notes", x => x.id);
                    table.ForeignKey(
                        name: "FK_internal_booking_notes_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "care_homes",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_internal_booking_notes_booking_id_created_on_utc",
                schema: "care_homes",
                table: "internal_booking_notes",
                columns: new[] { "booking_id", "created_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "internal_booking_notes",
                schema: "care_homes");

            migrationBuilder.DropColumn(
                name: "payment_completed_on_utc",
                schema: "care_homes",
                table: "bookings");
        }
    }
}
