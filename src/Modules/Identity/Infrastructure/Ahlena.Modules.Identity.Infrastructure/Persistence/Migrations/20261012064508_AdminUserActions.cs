using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminUserActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_user_actions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_user_actions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_user_actions_action_created_on_utc",
                schema: "identity",
                table: "admin_user_actions",
                columns: new[] { "action", "created_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_user_actions_actor_user_id",
                schema: "identity",
                table: "admin_user_actions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_user_actions_target_user_id",
                schema: "identity",
                table: "admin_user_actions",
                column: "target_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_user_actions",
                schema: "identity");
        }
    }
}
