using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeRoomTypeAmenitiesAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "amenities_json",
                schema: "care_homes",
                table: "room_types",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            // Pre-existing room types materialize through the JSON amenities converter,
            // which requires a JSON array; the column addition above would otherwise
            // backfill an empty string and make inventory reads fail to deserialize.
            migrationBuilder.Sql("UPDATE care_homes.room_types SET amenities_json = '[]' WHERE amenities_json = '';");

            migrationBuilder.CreateTable(
                name: "room_type_media",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_type_media", x => x.id);
                    table.ForeignKey(
                        name: "FK_room_type_media_room_types_room_type_id",
                        column: x => x.room_type_id,
                        principalSchema: "care_homes",
                        principalTable: "room_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_room_type_media_room_type_id_kind_position",
                schema: "care_homes",
                table: "room_type_media",
                columns: new[] { "room_type_id", "kind", "position" },
                unique: true);

            // Phase 2: per-room display-name override + per-room media.
            migrationBuilder.AddColumn<string>(
                name: "arabic_display_name",
                schema: "care_homes",
                table: "rooms",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "english_display_name",
                schema: "care_homes",
                table: "rooms",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "room_media",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_media", x => x.id);
                    table.ForeignKey(
                        name: "FK_room_media_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "care_homes",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_room_media_room_id_kind_position",
                schema: "care_homes",
                table: "room_media",
                columns: new[] { "room_id", "kind", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_media",
                schema: "care_homes");

            migrationBuilder.DropColumn(
                name: "arabic_display_name",
                schema: "care_homes",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "english_display_name",
                schema: "care_homes",
                table: "rooms");

            migrationBuilder.DropTable(
                name: "room_type_media",
                schema: "care_homes");

            migrationBuilder.DropColumn(
                name: "amenities_json",
                schema: "care_homes",
                table: "room_types");
        }
    }
}
