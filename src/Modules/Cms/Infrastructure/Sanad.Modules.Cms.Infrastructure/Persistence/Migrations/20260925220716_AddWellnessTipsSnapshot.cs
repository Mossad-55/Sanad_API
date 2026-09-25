using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWellnessTipsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wellness_tips",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wellness_tips", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wellness_tip_sections",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    arabic_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    english_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    wellness_tip_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wellness_tip_sections", x => x.id);
                    table.ForeignKey(
                        name: "FK_wellness_tip_sections_wellness_tips_wellness_tip_id",
                        column: x => x.wellness_tip_id,
                        principalSchema: "cms",
                        principalTable: "wellness_tips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wellness_tip_sections_wellness_tip_id_display_order",
                schema: "cms",
                table: "wellness_tip_sections",
                columns: new[] { "wellness_tip_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wellness_tips_status_category_updated_on_utc",
                schema: "cms",
                table: "wellness_tips",
                columns: new[] { "status", "category", "updated_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wellness_tip_sections",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "wellness_tips",
                schema: "cms");
        }
    }
}
