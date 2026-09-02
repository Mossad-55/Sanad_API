using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareAssessmentQuiz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assessment_questions",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    arabic_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessment_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assessment_tiers",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    screen_order = table.Column<int>(type: "integer", nullable: false),
                    arabic_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    arabic_subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    english_subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    background_color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    arabic_button_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    english_button_text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    min_score = table.Column<int>(type: "integer", nullable: false),
                    max_score = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    arabic_recommendations = table.Column<string>(type: "text", nullable: false),
                    english_recommendations = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessment_tiers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "care_assessments",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assessment_tier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_score = table.Column<int>(type: "integer", nullable: false),
                    completed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_care_assessments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assessment_options",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    arabic_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    english_text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    weight = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessment_options", x => x.id);
                    table.ForeignKey(
                        name: "FK_assessment_options_assessment_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "families",
                        principalTable: "assessment_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "care_assessment_answers",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score_snapshot = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_care_assessment_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_care_assessment_answers_care_assessments_assessment_id",
                        column: x => x.assessment_id,
                        principalSchema: "families",
                        principalTable: "care_assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_assessment_options_question_id_order",
                schema: "families",
                table: "assessment_options",
                columns: new[] { "question_id", "order" });

            migrationBuilder.CreateIndex(
                name: "IX_assessment_questions_is_active_order",
                schema: "families",
                table: "assessment_questions",
                columns: new[] { "is_active", "order" });

            migrationBuilder.CreateIndex(
                name: "IX_assessment_tiers_is_active_screen_order",
                schema: "families",
                table: "assessment_tiers",
                columns: new[] { "is_active", "screen_order" });

            migrationBuilder.CreateIndex(
                name: "IX_care_assessment_answers_assessment_id",
                schema: "families",
                table: "care_assessment_answers",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "IX_care_assessments_elderly_id",
                schema: "families",
                table: "care_assessments",
                column: "elderly_id");

            migrationBuilder.CreateIndex(
                name: "IX_care_assessments_family_id",
                schema: "families",
                table: "care_assessments",
                column: "family_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment_options",
                schema: "families");

            migrationBuilder.DropTable(
                name: "assessment_tiers",
                schema: "families");

            migrationBuilder.DropTable(
                name: "care_assessment_answers",
                schema: "families");

            migrationBuilder.DropTable(
                name: "assessment_questions",
                schema: "families");

            migrationBuilder.DropTable(
                name: "care_assessments",
                schema: "families");
        }
    }
}
