using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyNotesAndActivityLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elderly_activity_logs",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_type = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_activity_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "elderly_notes",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_notes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_activity_logs_elderly_id_created_on_utc",
                schema: "families",
                table: "elderly_activity_logs",
                columns: new[] { "elderly_id", "created_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_notes_created_on_utc",
                schema: "families",
                table: "elderly_notes",
                column: "created_on_utc");

            migrationBuilder.CreateIndex(
                name: "IX_elderly_notes_elderly_id",
                schema: "families",
                table: "elderly_notes",
                column: "elderly_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elderly_activity_logs",
                schema: "families");

            migrationBuilder.DropTable(
                name: "elderly_notes",
                schema: "families");
        }
    }
}
