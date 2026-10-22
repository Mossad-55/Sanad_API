using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "site_settings",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    navbar_links_json = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    arabic_seo_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    english_seo_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    arabic_seo_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    english_seo_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_settings", x => x.id);
                    table.CheckConstraint("ck_site_settings_single_row", "\"id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_settings",
                schema: "cms");
        }
    }
}
