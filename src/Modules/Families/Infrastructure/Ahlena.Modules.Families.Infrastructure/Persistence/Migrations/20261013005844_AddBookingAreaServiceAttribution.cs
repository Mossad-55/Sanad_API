using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingAreaServiceAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "booking_area_id",
                schema: "families",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "booking_service_id",
                schema: "families",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingAreaId_BookingServiceId",
                schema: "families",
                table: "bookings",
                columns: new[] { "booking_area_id", "booking_service_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_BookingAreaId_BookingServiceId",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "booking_area_id",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "booking_service_id",
                schema: "families",
                table: "bookings");
        }
    }
}
