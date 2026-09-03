using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicationsAndDoseLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medication_dose_logs",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    taken_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    skipped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medication_dose_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "medications",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dosage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    dose_unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    dose_quantity = table.Column<int>(type: "integer", nullable: false),
                    dose_times = table.Column<TimeOnly[]>(type: "time without time zone[]", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_quantity = table.Column<int>(type: "integer", nullable: true),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medication_dose_logs_elderly_id_scheduled_date",
                schema: "families",
                table: "medication_dose_logs",
                columns: new[] { "elderly_id", "scheduled_date" });

            migrationBuilder.CreateIndex(
                name: "IX_medication_dose_logs_medication_id",
                schema: "families",
                table: "medication_dose_logs",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "IX_medication_dose_logs_status",
                schema: "families",
                table: "medication_dose_logs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_medications_elderly_id",
                schema: "families",
                table: "medications",
                column: "elderly_id");

            migrationBuilder.CreateIndex(
                name: "IX_medications_status",
                schema: "families",
                table: "medications",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medication_dose_logs",
                schema: "families");

            migrationBuilder.DropTable(
                name: "medications",
                schema: "families");
        }
    }
}
