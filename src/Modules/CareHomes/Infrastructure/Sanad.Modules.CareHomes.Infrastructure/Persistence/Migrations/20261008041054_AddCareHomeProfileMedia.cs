using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeProfileMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profile_media",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_media", x => x.id);
                    table.ForeignKey(
                        name: "FK_profile_media_profile_revisions_profile_revision_id",
                        column: x => x.profile_revision_id,
                        principalSchema: "care_homes",
                        principalTable: "profile_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_profile_media_profile_revision_id_kind_position",
                schema: "care_homes",
                table: "profile_media",
                columns: new[] { "profile_revision_id", "kind", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_media",
                schema: "care_homes");
        }
    }
}
