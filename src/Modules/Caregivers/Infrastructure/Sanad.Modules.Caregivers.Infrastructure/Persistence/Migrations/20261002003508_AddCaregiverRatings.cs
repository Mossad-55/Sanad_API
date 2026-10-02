using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caregiver_ratings",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stars = table.Column<int>(type: "integer", nullable: false),
                    review_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_ratings", x => x.id);
                    table.ForeignKey(
                        name: "FK_caregiver_ratings_caregivers_caregiver_id",
                        column: x => x.caregiver_id,
                        principalSchema: "caregivers",
                        principalTable: "caregivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_ratings_booking_id",
                schema: "caregivers",
                table: "caregiver_ratings",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_ratings_caregiver_id_stars",
                schema: "caregivers",
                table: "caregiver_ratings",
                columns: new[] { "caregiver_id", "stars" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_ratings",
                schema: "caregivers");
        }
    }
}
