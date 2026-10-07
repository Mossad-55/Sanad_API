using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalAccessGrantGrantee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MedicalAccessGrants_DependentId",
                schema: "families",
                table: "MedicalAccessGrants");

            migrationBuilder.AddColumn<Guid>(
                name: "GranteeUserId",
                schema: "families",
                table: "MedicalAccessGrants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAccessGrants_DependentId_GranteeUserId",
                schema: "families",
                table: "MedicalAccessGrants",
                columns: new[] { "DependentId", "GranteeUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MedicalAccessGrants_DependentId_GranteeUserId",
                schema: "families",
                table: "MedicalAccessGrants");

            migrationBuilder.DropColumn(
                name: "GranteeUserId",
                schema: "families",
                table: "MedicalAccessGrants");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalAccessGrants_DependentId",
                schema: "families",
                table: "MedicalAccessGrants",
                column: "DependentId",
                unique: true,
                filter: "\"RevokedOnUtc\" IS NULL");
        }
    }
}
