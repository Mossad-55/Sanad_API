using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_blocks",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Target = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_blocks", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_blocks_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "room_types",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArabicName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnglishName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArabicDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EnglishDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    MonthlyPriceEgp = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocationMode = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_types", x => x.id);
                    table.ForeignKey(
                        name: "FK_room_types_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.id);
                    table.ForeignKey(
                        name: "FK_rooms_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rooms_room_types_room_type_id",
                        column: x => x.room_type_id,
                        principalSchema: "care_homes",
                        principalTable: "room_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beds",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beds", x => x.id);
                    table.ForeignKey(
                        name: "FK_beds_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_beds_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "care_homes",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_beds_care_home_id",
                schema: "care_homes",
                table: "beds",
                column: "care_home_id");

            migrationBuilder.CreateIndex(
                name: "ux_care_homes_beds_room_label",
                schema: "care_homes",
                table: "beds",
                columns: new[] { "room_id", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_blocks_care_home_id_Target_target_id",
                schema: "care_homes",
                table: "maintenance_blocks",
                columns: new[] { "care_home_id", "Target", "target_id" });

            migrationBuilder.CreateIndex(
                name: "IX_room_types_care_home_id_EnglishName",
                schema: "care_homes",
                table: "room_types",
                columns: new[] { "care_home_id", "EnglishName" });

            migrationBuilder.CreateIndex(
                name: "IX_rooms_room_type_id",
                schema: "care_homes",
                table: "rooms",
                column: "room_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_care_homes_rooms_facility_room_number",
                schema: "care_homes",
                table: "rooms",
                columns: new[] { "care_home_id", "RoomNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beds",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "maintenance_blocks",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "rooms",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "room_types",
                schema: "care_homes");
        }
    }
}
