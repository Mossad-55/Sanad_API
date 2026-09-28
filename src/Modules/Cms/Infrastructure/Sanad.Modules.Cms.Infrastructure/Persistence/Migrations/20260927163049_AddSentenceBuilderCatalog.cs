using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentenceBuilderCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sentence_builder_catalog_entries",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StableKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sentence_builder_catalog_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sentence_builder_catalog_revisions",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ArabicLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnglishLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sentence_builder_catalog_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sentence_builder_catalog_revisions_sentence_builder_catalog~",
                        column: x => x.CatalogEntryId,
                        principalSchema: "cms",
                        principalTable: "sentence_builder_catalog_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sentence_builder_catalog_entries_StableKey_Category",
                schema: "cms",
                table: "sentence_builder_catalog_entries",
                columns: new[] { "StableKey", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sentence_builder_catalog_revisions_CatalogEntryId_IsActive",
                schema: "cms",
                table: "sentence_builder_catalog_revisions",
                columns: new[] { "CatalogEntryId", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_sentence_builder_catalog_revisions_CatalogEntryId_Version",
                schema: "cms",
                table: "sentence_builder_catalog_revisions",
                columns: new[] { "CatalogEntryId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sentence_builder_catalog_revisions",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "sentence_builder_catalog_entries",
                schema: "cms");
        }
    }
}
