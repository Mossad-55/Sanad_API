using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationPreferencesSuperset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "family_activity_alerts",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "messages_from_families",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "new_orders",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "system_notifications",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "family_activity_alerts",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "messages_from_families",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "new_orders",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "system_notifications",
                schema: "identity",
                table: "users");
        }
    }
}
