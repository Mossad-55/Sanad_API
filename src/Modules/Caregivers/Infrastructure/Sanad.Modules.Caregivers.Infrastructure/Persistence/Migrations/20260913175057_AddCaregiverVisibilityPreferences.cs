using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverVisibilityPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "share_location",
                schema: "caregivers",
                table: "caregivers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_phone",
                schema: "caregivers",
                table: "caregivers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_profile",
                schema: "caregivers",
                table: "caregivers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_rating",
                schema: "caregivers",
                table: "caregivers",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "share_location",
                schema: "caregivers",
                table: "caregivers");

            migrationBuilder.DropColumn(
                name: "show_phone",
                schema: "caregivers",
                table: "caregivers");

            migrationBuilder.DropColumn(
                name: "show_profile",
                schema: "caregivers",
                table: "caregivers");

            migrationBuilder.DropColumn(
                name: "show_rating",
                schema: "caregivers",
                table: "caregivers");
        }
    }
}
