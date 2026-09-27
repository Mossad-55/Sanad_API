using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "notifications",
                table: "notifications",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_idempotency_key",
                schema: "notifications",
                table: "notifications",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_idempotency_key",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "notifications",
                table: "notifications");
        }
    }
}
