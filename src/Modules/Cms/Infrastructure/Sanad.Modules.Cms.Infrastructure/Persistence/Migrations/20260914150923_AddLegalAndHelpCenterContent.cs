using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalAndHelpCenterContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "help_faqs",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    audience = table.Column<int>(type: "integer", nullable: false),
                    arabic_question = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    english_question = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    arabic_answer = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    english_answer = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_help_faqs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "legal_documents",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    audience = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "support_contacts",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    support_phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    support_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_contacts", x => x.id);
                    table.CheckConstraint("ck_support_contacts_single_row", "\"id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "legal_sections",
                schema: "cms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_type = table.Column<int>(type: "integer", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    arabic_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    english_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    arabic_bullets = table.Column<string>(type: "text", nullable: false),
                    english_bullets = table.Column<string>(type: "text", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_sections", x => x.id);
                    table.ForeignKey(
                        name: "FK_legal_sections_legal_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "cms",
                        principalTable: "legal_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_help_faqs_audience_is_active_display_order",
                schema: "cms",
                table: "help_faqs",
                columns: new[] { "audience", "is_active", "display_order" });

            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_document_type_audience_status",
                schema: "cms",
                table: "legal_documents",
                columns: new[] { "document_type", "audience", "status" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_document_type_audience_version",
                schema: "cms",
                table: "legal_documents",
                columns: new[] { "document_type", "audience", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_status_document_type_audience_version",
                schema: "cms",
                table: "legal_documents",
                columns: new[] { "status", "document_type", "audience", "version" });

            migrationBuilder.CreateIndex(
                name: "IX_legal_sections_document_id_display_order",
                schema: "cms",
                table: "legal_sections",
                columns: new[] { "document_id", "display_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "help_faqs",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "legal_sections",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "support_contacts",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "legal_documents",
                schema: "cms");
        }
    }
}
