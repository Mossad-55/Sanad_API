using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareHomeRatingModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "hidden_by_user_id",
                schema: "care_homes",
                table: "ratings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "hidden_on_utc",
                schema: "care_homes",
                table: "ratings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hide_reason",
                schema: "care_homes",
                table: "ratings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "moderation_state",
                schema: "care_homes",
                table: "ratings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "rating_flags",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating_id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_flags", x => x.id);
                    table.ForeignKey(
                        name: "FK_rating_flags_ratings_rating_id",
                        column: x => x.rating_id,
                        principalSchema: "care_homes",
                        principalTable: "ratings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rating_moderation_actions",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating_id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_moderation_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_rating_moderation_actions_ratings_rating_id",
                        column: x => x.rating_id,
                        principalSchema: "care_homes",
                        principalTable: "ratings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ratings_care_home_id_moderation_state",
                schema: "care_homes",
                table: "ratings",
                columns: new[] { "care_home_id", "moderation_state" });

            migrationBuilder.CreateIndex(
                name: "IX_rating_flags_rating_id_owner_user_id",
                schema: "care_homes",
                table: "rating_flags",
                columns: new[] { "rating_id", "owner_user_id" },
                unique: true,
                filter: "\"status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_rating_flags_status",
                schema: "care_homes",
                table: "rating_flags",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_rating_moderation_actions_rating_id_occurred_on_utc",
                schema: "care_homes",
                table: "rating_moderation_actions",
                columns: new[] { "rating_id", "occurred_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rating_flags",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "rating_moderation_actions",
                schema: "care_homes");

            migrationBuilder.DropIndex(
                name: "IX_ratings_care_home_id_moderation_state",
                schema: "care_homes",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "hidden_by_user_id",
                schema: "care_homes",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "hidden_on_utc",
                schema: "care_homes",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "hide_reason",
                schema: "care_homes",
                table: "ratings");

            migrationBuilder.DropColumn(
                name: "moderation_state",
                schema: "care_homes",
                table: "ratings");
        }
    }
}
