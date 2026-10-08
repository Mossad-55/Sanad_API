using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeBookingExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "extension_of_booking_id",
                schema: "care_homes",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "extension_root_booking_id",
                schema: "care_homes",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_extension_of_booking_id",
                schema: "care_homes",
                table: "bookings",
                column: "extension_of_booking_id");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_extension_root_booking_id_StartDate",
                schema: "care_homes",
                table: "bookings",
                columns: new[] { "extension_root_booking_id", "StartDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_bookings_extension_of_booking_id",
                schema: "care_homes",
                table: "bookings",
                column: "extension_of_booking_id",
                principalSchema: "care_homes",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_bookings_extension_root_booking_id",
                schema: "care_homes",
                table: "bookings",
                column: "extension_root_booking_id",
                principalSchema: "care_homes",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_bookings_extension_of_booking_id",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_bookings_bookings_extension_root_booking_id",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_extension_of_booking_id",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_bookings_extension_root_booking_id_StartDate",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "extension_of_booking_id",
                schema: "care_homes",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "extension_root_booking_id",
                schema: "care_homes",
                table: "bookings");
        }
    }
}
