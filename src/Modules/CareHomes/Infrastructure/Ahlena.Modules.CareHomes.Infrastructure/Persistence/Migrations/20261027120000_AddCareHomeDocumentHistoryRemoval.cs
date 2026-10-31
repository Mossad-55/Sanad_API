using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeDocumentHistoryRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // History-only logical removal (2026-10-27), Owner-approved
            // scope: nullable audit columns for the logical delete on the
            // care_homes documents table. NONDESTRUCTIVE — no data columns
            // change, no row is touched, and the read layer only hides rows
            // where removed_on_utc is set after the new API is used.
            migrationBuilder.AddColumn<Guid?>(
                name: "removed_by_user_id",
                schema: "care_homes",
                table: "documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime?>(
                name: "removed_on_utc",
                schema: "care_homes",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "removed_on_utc",
                schema: "care_homes",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "removed_by_user_id",
                schema: "care_homes",
                table: "documents");
        }
    }
}
