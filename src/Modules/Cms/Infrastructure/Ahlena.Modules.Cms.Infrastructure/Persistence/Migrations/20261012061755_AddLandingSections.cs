using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "landing_sections",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    arabic_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    english_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    content_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_visible = table.Column<bool>(type: "boolean", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_sections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_landing_sections_status_display_order",
                schema: "cms",
                table: "landing_sections",
                columns: new[] { "status", "display_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "landing_sections",
                schema: "cms");
        }
    }
}
