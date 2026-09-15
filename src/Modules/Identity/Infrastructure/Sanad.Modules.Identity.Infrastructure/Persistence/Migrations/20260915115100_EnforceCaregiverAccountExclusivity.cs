using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCaregiverAccountExclusivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_user_accounts_one_caregiver",
                schema: "identity",
                table: "user_accounts",
                column: "user_id",
                unique: true,
                filter: "account_type IN (2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_user_accounts_one_caregiver",
                schema: "identity",
                table: "user_accounts");
        }
    }
}
