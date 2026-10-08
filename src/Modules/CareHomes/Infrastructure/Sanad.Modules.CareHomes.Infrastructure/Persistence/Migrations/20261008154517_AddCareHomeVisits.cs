using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visit_settings",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_capacity = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    operating_hours_json = table.Column<string>(type: "jsonb", nullable: false),
                    visiting_windows_json = table.Column<string>(type: "jsonb", nullable: false),
                    closures_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_visit_settings_facilities_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visitor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    visitor_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visitor_count = table.Column<int>(type: "integer", nullable: false),
                    starts_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reschedule_of_visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => x.id);
                    table.ForeignKey(
                        name: "FK_visits_facilities_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_visits_visits_reschedule_of_visit_id",
                        column: x => x.reschedule_of_visit_id,
                        principalSchema: "care_homes",
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visit_settings_facility_id",
                schema: "care_homes",
                table: "visit_settings",
                column: "facility_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_visits_facility_id_status_starts_at_utc",
                schema: "care_homes",
                table: "visits",
                columns: new[] { "facility_id", "status", "starts_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_visits_family_id_status_starts_at_utc",
                schema: "care_homes",
                table: "visits",
                columns: new[] { "family_id", "status", "starts_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_visits_reschedule_of_visit_id_status",
                schema: "care_homes",
                table: "visits",
                columns: new[] { "reschedule_of_visit_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visit_settings",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "visits",
                schema: "care_homes");
        }
    }
}
