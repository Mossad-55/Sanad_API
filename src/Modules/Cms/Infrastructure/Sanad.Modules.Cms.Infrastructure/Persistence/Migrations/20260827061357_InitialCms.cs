using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cms");

            migrationBuilder.CreateTable(
                name: "splash_screens",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    internal_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    audience = table.Column<int>(type: "integer", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    arabic_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    arabic_button_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    english_button_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    background_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_splash_screens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_splash_screens_audience_status_display_order",
                schema: "cms",
                table: "splash_screens",
                columns: new[] { "audience", "status", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_splash_screens_internal_name",
                schema: "cms",
                table: "splash_screens",
                column: "internal_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "splash_screens",
                schema: "cms");
        }
    }
}
