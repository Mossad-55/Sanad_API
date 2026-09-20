using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_reports",
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
                    systolic = table.Column<int>(type: "integer", nullable: true),
                    diastolic = table.Column<int>(type: "integer", nullable: true),
                    pulse = table.Column<int>(type: "integer", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    measurement_taken_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    assessment = table.Column<int>(type: "integer", nullable: false),
                    submitted_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    photo_consent_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    photo_consent_attested_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    photo_consent_caregiver_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    photo_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_reports", x => x.id);
                    table.ForeignKey(
                        name: "FK_medical_reports_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "families",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_reports_elderlies_elderly_id",
                        column: x => x.elderly_id,
                        principalSchema: "families",
                        principalTable: "elderlies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_medical_reports_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "families",
                        principalTable: "families",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medical_reports_elderly_id",
                schema: "families",
                table: "medical_reports",
                column: "elderly_id");

            migrationBuilder.CreateIndex(
                name: "IX_medical_reports_family_id_submitted_on_utc",
                schema: "families",
                table: "medical_reports",
                columns: new[] { "family_id", "submitted_on_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_medical_reports_booking",
                schema: "families",
                table: "medical_reports",
                column: "booking_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_reports",
                schema: "families");
        }
    }
}
