using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyWelcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elderly_welcomes",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    arabic_cta_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_cta_label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cta_action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_welcomes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "elderly_welcome_benefits",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    arabic_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    elderly_welcome_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_welcome_benefits", x => x.id);
                    table.ForeignKey(
                        name: "FK_elderly_welcome_benefits_elderly_welcomes_elderly_welcome_id",
                        column: x => x.elderly_welcome_id,
                        principalSchema: "cms",
                        principalTable: "elderly_welcomes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_welcome_benefits_elderly_welcome_id_display_order",
                schema: "cms",
                table: "elderly_welcome_benefits",
                columns: new[] { "elderly_welcome_id", "display_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elderly_welcomes_status",
                schema: "cms",
                table: "elderly_welcomes",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elderly_welcome_benefits",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "elderly_welcomes",
                schema: "cms");
        }
    }
}
