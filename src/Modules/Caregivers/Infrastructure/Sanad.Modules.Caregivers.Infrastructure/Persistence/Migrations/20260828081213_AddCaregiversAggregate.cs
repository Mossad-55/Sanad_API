using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiversAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caregivers",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    detailed_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medical_professional_title_id = table.Column<Guid>(type: "uuid", nullable: true),
                    medical_years_of_experience = table.Column<int>(type: "integer", nullable: true),
                    medical_specialization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    medical_academic_degree_id = table.Column<Guid>(type: "uuid", nullable: true),
                    medical_current_workplace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    medical_biography = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    companion_years_of_experience = table.Column<int>(type: "integer", nullable: true),
                    companion_specialization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    companion_biography = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    availability = table.Column<int>(type: "integer", nullable: false),
                    medical_home_visit_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    medical_eight_hour_shift_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    medical_twelve_hour_shift_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    medical_twenty_four_hour_shift_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    companion_hourly_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    companion_eight_hour_day_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    companion_overnight_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    average_rating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    reviews_count = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregivers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "caregiver_area_selections",
                schema: "caregivers",
                columns: table => new
                {
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_area_selections", x => new { x.caregiver_id, x.area_id });
                    table.ForeignKey(
                        name: "FK_caregiver_area_selections_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "caregiver_certificates",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    file_path = table.Column<string>(type: "text", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<int>(type: "integer", nullable: false),
                    review_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_certificates", x => new { x.caregiver_id, x.id });
                    table.ForeignKey(
                        name: "FK_caregiver_certificates_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "caregiver_language_selections",
                schema: "caregivers",
                columns: table => new
                {
                    language_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_language_selections", x => new { x.caregiver_id, x.language_id });
                    table.ForeignKey(
                        name: "FK_caregiver_language_selections_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "caregiver_service_selections",
                schema: "caregivers",
                columns: table => new
                {
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_service_selections", x => new { x.caregiver_id, x.service_id });
                    table.ForeignKey(
                        name: "FK_caregiver_service_selections_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "companion_schedules",
                schema: "caregivers",
                columns: table => new
                {
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_schedules", x => x.caregiver_id);
                    table.ForeignKey(
                        name: "FK_companion_schedules_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medical_schedules",
                schema: "caregivers",
                columns: table => new
                {
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_schedules", x => x.caregiver_id);
                    table.ForeignKey(
                        name: "FK_medical_schedules_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "companion_availability_windows",
                schema: "caregivers",
                columns: table => new
                {
                    companion_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    booking_type = table.Column<int>(type: "integer", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_availability_windows", x => new { x.companion_schedule_id, x.Id });
                    table.ForeignKey(
                        name: "FK_companion_availability_windows_companion_schedules_companio~",
                        column: x => x.companion_schedule_id,
                        principalSchema: "caregivers",
                        principalTable: "companion_schedules",
                        principalColumn: "caregiver_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medical_home_visit_windows",
                schema: "caregivers",
                columns: table => new
                {
                    medical_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_home_visit_windows", x => new { x.medical_schedule_id, x.Id });
                    table.ForeignKey(
                        name: "FK_medical_home_visit_windows_medical_schedules_medical_schedu~",
                        column: x => x.medical_schedule_id,
                        principalSchema: "caregivers",
                        principalTable: "medical_schedules",
                        principalColumn: "caregiver_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medical_shifts",
                schema: "caregivers",
                columns: table => new
                {
                    medical_schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    shift_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_shifts", x => new { x.medical_schedule_id, x.Id });
                    table.ForeignKey(
                        name: "FK_medical_shifts_medical_schedules_medical_schedule_id",
                        column: x => x.medical_schedule_id,
                        principalSchema: "caregivers",
                        principalTable: "medical_schedules",
                        principalColumn: "caregiver_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_caregivers_user_id",
                schema: "caregivers",
                table: "caregivers",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_area_selections",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "caregiver_certificates",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "caregiver_language_selections",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "caregiver_service_selections",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "companion_availability_windows",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "medical_home_visit_windows",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "medical_shifts",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "companion_schedules",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "medical_schedules",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "caregivers",
                schema: "caregivers");
        }
    }
}
