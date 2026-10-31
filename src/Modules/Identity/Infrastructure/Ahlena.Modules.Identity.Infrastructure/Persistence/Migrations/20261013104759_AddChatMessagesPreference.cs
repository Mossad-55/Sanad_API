using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessagesPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backward-compatible rollout: legacy rows adopt the existing
            // messages_from_families opt-out; the column defaults true for
            // rows inserted without an explicit value (new users start
            // enabled via the domain default as well).
            migrationBuilder.AddColumn<bool>(
                name: "chat_messages",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "identity"."users"
                SET "chat_messages" = "messages_from_families";
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "chat_messages",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chat_messages",
                schema: "identity",
                table: "users");
        }
    }
}
