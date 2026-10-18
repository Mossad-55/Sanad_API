using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverMedicalAccessAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caregiver_medical_access_audits",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dependent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    grant_type = table.Column<int>(type: "integer", nullable: true),
                    can_view_records = table.Column<bool>(type: "boolean", nullable: true),
                    can_edit_records = table.Column<bool>(type: "boolean", nullable: true),
                    can_share_with_others = table.Column<bool>(type: "boolean", nullable: true),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_medical_access_audits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_medical_access_audits_actor_user_id_occurred_on_u~",
                schema: "families",
                table: "caregiver_medical_access_audits",
                columns: new[] { "actor_user_id", "occurred_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_medical_access_audits_dependent_id_occurred_on_utc",
                schema: "families",
                table: "caregiver_medical_access_audits",
                columns: new[] { "dependent_id", "occurred_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_medical_access_audits_occurred_on_utc",
                schema: "families",
                table: "caregiver_medical_access_audits",
                column: "occurred_on_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_medical_access_audits",
                schema: "families");
        }
    }
}
