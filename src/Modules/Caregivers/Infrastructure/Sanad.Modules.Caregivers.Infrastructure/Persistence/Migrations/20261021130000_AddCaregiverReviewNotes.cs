using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverReviewNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caregiver_review_notes",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_review_notes", x => x.id);
                    table.ForeignKey(
                        name: "FK_caregiver_review_notes_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_review_notes_caregiver_id_created_on_utc",
                schema: "caregivers",
                table: "caregiver_review_notes",
                columns: new[] { "caregiver_id", "created_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_review_notes",
                schema: "caregivers");
        }
    }
}
