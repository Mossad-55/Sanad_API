using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyHelpRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elderly_help_requests",
                schema: "families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElderlyIdentityUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ActorArabicLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorEnglishLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ActionArabicLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActionEnglishLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NeedKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NeedArabicLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NeedEnglishLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    QualifierKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    QualifierArabicLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    QualifierEnglishLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_help_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elderly_help_request_history",
                schema: "families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HelpRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_help_request_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_elderly_help_request_history_elderly_help_requests_HelpRequ~",
                        column: x => x.HelpRequestId,
                        principalSchema: "families",
                        principalTable: "elderly_help_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_help_request_history_HelpRequestId_OccurredOnUtc",
                schema: "families",
                table: "elderly_help_request_history",
                columns: new[] { "HelpRequestId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_help_requests_CreatedOnUtc",
                schema: "families",
                table: "elderly_help_requests",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_elderly_help_requests_ElderlyIdentityUserId_IdempotencyKey",
                schema: "families",
                table: "elderly_help_requests",
                columns: new[] { "ElderlyIdentityUserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elderly_help_request_history",
                schema: "families");

            migrationBuilder.DropTable(
                name: "elderly_help_requests",
                schema: "families");
        }
    }
}
