using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueMedicationDoseSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_medication_dose_logs_medication_id_scheduled_date_scheduled~",
                schema: "families",
                table: "medication_dose_logs",
                columns: new[] { "medication_id", "scheduled_date", "scheduled_time" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_medication_dose_logs_medication_id_scheduled_date_scheduled~",
                schema: "families",
                table: "medication_dose_logs");
        }
    }
}
