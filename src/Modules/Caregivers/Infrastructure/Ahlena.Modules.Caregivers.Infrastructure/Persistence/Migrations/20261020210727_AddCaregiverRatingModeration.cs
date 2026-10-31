using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverRatingModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "hidden_by_user_id",
                schema: "caregivers",
                table: "caregiver_ratings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "hidden_on_utc",
                schema: "caregivers",
                table: "caregiver_ratings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hide_reason",
                schema: "caregivers",
                table: "caregiver_ratings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "moderation_state",
                schema: "caregivers",
                table: "caregiver_ratings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "caregiver_rating_moderation_actions",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_rating_moderation_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_caregiver_rating_moderation_actions_caregiver_ratings_ratin~",
                        column: x => x.rating_id,
                        principalSchema: "caregivers",
                        principalTable: "caregiver_ratings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_ratings_caregiver_id_moderation_state",
                schema: "caregivers",
                table: "caregiver_ratings",
                columns: new[] { "caregiver_id", "moderation_state" });

            migrationBuilder.CreateIndex(
                name: "IX_caregiver_rating_moderation_actions_rating_id_occurred_on_u~",
                schema: "caregivers",
                table: "caregiver_rating_moderation_actions",
                columns: new[] { "rating_id", "occurred_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_rating_moderation_actions",
                schema: "caregivers");

            migrationBuilder.DropIndex(
                name: "IX_caregiver_ratings_caregiver_id_moderation_state",
                schema: "caregivers",
                table: "caregiver_ratings");

            migrationBuilder.DropColumn(
                name: "hidden_by_user_id",
                schema: "caregivers",
                table: "caregiver_ratings");

            migrationBuilder.DropColumn(
                name: "hidden_on_utc",
                schema: "caregivers",
                table: "caregiver_ratings");

            migrationBuilder.DropColumn(
                name: "hide_reason",
                schema: "caregivers",
                table: "caregiver_ratings");

            migrationBuilder.DropColumn(
                name: "moderation_state",
                schema: "caregivers",
                table: "caregiver_ratings");
        }
    }
}
