using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_categories",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "library_content_interactions",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_content_interactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "library_contents",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    arabic_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    arabic_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    english_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    video_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    cover_image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_premium = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    view_count = table.Column<int>(type: "integer", nullable: false),
                    likes_count = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_contents", x => x.id);
                    table.ForeignKey(
                        name: "FK_library_contents_library_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "cms",
                        principalTable: "library_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "library_content_sections",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    arabic_bullets = table.Column<string>(type: "jsonb", nullable: false),
                    english_bullets = table.Column<string>(type: "jsonb", nullable: false),
                    library_content_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_content_sections", x => x.id);
                    table.ForeignKey(
                        name: "FK_library_content_sections_library_contents_library_content_id",
                        column: x => x.library_content_id,
                        principalSchema: "cms",
                        principalTable: "library_contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_content_interactions_content_id_user_id_kind",
                schema: "cms",
                table: "library_content_interactions",
                columns: new[] { "content_id", "user_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_library_content_interactions_user_id_kind",
                schema: "cms",
                table: "library_content_interactions",
                columns: new[] { "user_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_library_content_sections_library_content_id_display_order",
                schema: "cms",
                table: "library_content_sections",
                columns: new[] { "library_content_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_library_contents_category_id",
                schema: "cms",
                table: "library_contents",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_library_contents_status_created_on_utc",
                schema: "cms",
                table: "library_contents",
                columns: new[] { "status", "created_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_library_contents_status_view_count",
                schema: "cms",
                table: "library_contents",
                columns: new[] { "status", "view_count" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_content_interactions",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "library_content_sections",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "library_contents",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "library_categories",
                schema: "cms");
        }
    }
}
