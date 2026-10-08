using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverPayoutPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caregiver_payout_policies",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payout_delay_hours = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_payout_policies", x => x.id);
                    table.CheckConstraint("ck_caregiver_payout_policy_delay", "payout_delay_hours >= 0");
                    table.CheckConstraint("ck_caregiver_payout_policy_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_caregiver_payout_policy_active",
                schema: "finance",
                table: "caregiver_payout_policies",
                column: "is_active",
                unique: true,
                filter: "\"is_active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ux_caregiver_payout_policy_version",
                schema: "finance",
                table: "caregiver_payout_policies",
                column: "version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_payout_policies",
                schema: "finance");
        }
    }
}
