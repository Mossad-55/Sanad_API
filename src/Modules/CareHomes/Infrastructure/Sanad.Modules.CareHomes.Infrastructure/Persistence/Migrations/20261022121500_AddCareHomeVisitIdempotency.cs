using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeVisitIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                schema: "care_homes",
                table: "visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_visits_family_id_idempotency_key",
                schema: "care_homes",
                table: "visits",
                columns: new[] { "family_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_visits_family_id_idempotency_key",
                schema: "care_homes",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                schema: "care_homes",
                table: "visits");
        }
    }
}
