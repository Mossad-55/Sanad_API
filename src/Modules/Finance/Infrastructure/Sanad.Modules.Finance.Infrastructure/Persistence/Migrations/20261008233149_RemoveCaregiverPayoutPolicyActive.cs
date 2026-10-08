using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCaregiverPayoutPolicyActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_caregiver_payout_policy_active",
                schema: "finance",
                table: "caregiver_payout_policies");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "finance",
                table: "caregiver_payout_policies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "finance",
                table: "caregiver_payout_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ux_caregiver_payout_policy_active",
                schema: "finance",
                table: "caregiver_payout_policies",
                column: "is_active",
                unique: true,
                filter: "\"is_active\" = TRUE");
        }
    }
}
