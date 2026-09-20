using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visit_reports",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_type = table.Column<int>(type: "integer", nullable: false),
                    observed_condition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    activities = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    assessment = table.Column<int>(type: "integer", nullable: false),
                    started_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_visit_reports_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "families",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_reports_elderlies_elderly_id",
                        column: x => x.elderly_id,
                        principalSchema: "families",
                        principalTable: "elderlies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visit_reports_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "families",
                        principalTable: "families",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visit_reports_elderly_id",
                schema: "families",
                table: "visit_reports",
                column: "elderly_id");

            migrationBuilder.CreateIndex(
                name: "IX_visit_reports_family_id_submitted_on_utc",
                schema: "families",
                table: "visit_reports",
                columns: new[] { "family_id", "submitted_on_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_reports_booking",
                schema: "families",
                table: "visit_reports",
                column: "booking_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visit_reports",
                schema: "families");
        }
    }
}
