using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlySos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elderly_sos",
                schema: "families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElderlyIdentityUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocationConsentGranted = table.Column<bool>(type: "boolean", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_sos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elderly_sos_history",
                schema: "families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElderlySosId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_sos_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_elderly_sos_history_elderly_sos_ElderlySosId",
                        column: x => x.ElderlySosId,
                        principalSchema: "families",
                        principalTable: "elderly_sos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_sos_CreatedOnUtc",
                schema: "families",
                table: "elderly_sos",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_elderly_sos_ElderlyIdentityUserId_IdempotencyKey",
                schema: "families",
                table: "elderly_sos",
                columns: new[] { "ElderlyIdentityUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elderly_sos_history_ElderlySosId_OccurredOnUtc",
                schema: "families",
                table: "elderly_sos_history",
                columns: new[] { "ElderlySosId", "OccurredOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elderly_sos_history",
                schema: "families");

            migrationBuilder.DropTable(
                name: "elderly_sos",
                schema: "families");
        }
    }
}
