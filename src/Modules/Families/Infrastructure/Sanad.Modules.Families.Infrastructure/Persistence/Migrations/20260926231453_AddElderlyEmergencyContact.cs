using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyEmergencyContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_name",
                schema: "families",
                table: "elderlies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_phone_number",
                schema: "families",
                table: "elderlies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emergency_contact_relationship",
                schema: "families",
                table: "elderlies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "emergency_contact_name",
                schema: "families",
                table: "elderlies");

            migrationBuilder.DropColumn(
                name: "emergency_contact_phone_number",
                schema: "families",
                table: "elderlies");

            migrationBuilder.DropColumn(
                name: "emergency_contact_relationship",
                schema: "families",
                table: "elderlies");
        }
    }
}
