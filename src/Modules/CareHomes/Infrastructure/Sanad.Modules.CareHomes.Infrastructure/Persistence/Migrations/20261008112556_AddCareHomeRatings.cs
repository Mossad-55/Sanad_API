using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ratings",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_ratings", x => x.id);
                    table.ForeignKey(
                        name: "FK_ratings_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "care_homes",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ratings_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_booking_id",
                schema: "care_homes",
                table: "ratings",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ratings_care_home_id_stars",
                schema: "care_homes",
                table: "ratings",
                columns: new[] { "care_home_id", "stars" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ratings",
                schema: "care_homes");
        }
    }
}
