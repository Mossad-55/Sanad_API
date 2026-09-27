using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicationLatenessSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medication_lateness_settings",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_lateness_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medication_lateness_setting_revisions",
                schema: "cms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ThresholdMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_lateness_setting_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_medication_lateness_setting_revisions_medication_lateness_s~",
                        column: x => x.SettingId,
                        principalSchema: "cms",
                        principalTable: "medication_lateness_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medication_lateness_setting_revisions_SettingId_IsActive",
                schema: "cms",
                table: "medication_lateness_setting_revisions",
                columns: new[] { "SettingId", "IsActive" },
                unique: true,
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_medication_lateness_setting_revisions_SettingId_Version",
                schema: "cms",
                table: "medication_lateness_setting_revisions",
                columns: new[] { "SettingId", "Version" },
                unique: true);

            migrationBuilder.InsertData(
                schema: "cms",
                table: "medication_lateness_settings",
                column: "Id",
                value: new Guid("00000000-0000-0000-0000-000000000060"));

            migrationBuilder.InsertData(
                schema: "cms",
                table: "medication_lateness_setting_revisions",
                columns: new[] { "Id", "SettingId", "Version", "ThresholdMinutes", "IsActive", "CreatedOnUtc" },
                values: new object[]
                {
                    new Guid("00000000-0000-0000-0000-000000000061"),
                    new Guid("00000000-0000-0000-0000-000000000060"),
                    1,
                    60,
                    true,
                    new DateTime(2026, 9, 27, 0, 0, 0, DateTimeKind.Utc)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medication_lateness_setting_revisions",
                schema: "cms");

            migrationBuilder.DropTable(
                name: "medication_lateness_settings",
                schema: "cms");
        }
    }
}
