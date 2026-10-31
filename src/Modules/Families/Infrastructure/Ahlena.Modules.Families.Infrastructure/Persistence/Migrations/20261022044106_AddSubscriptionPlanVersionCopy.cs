using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlanVersionCopy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "arabic_description",
                schema: "families",
                table: "subscription_plan_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "arabic_name",
                schema: "families",
                table: "subscription_plan_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "english_description",
                schema: "families",
                table: "subscription_plan_versions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "english_name",
                schema: "families",
                table: "subscription_plan_versions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "arabic_label",
                schema: "families",
                table: "subscription_plan_version_benefits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "english_label",
                schema: "families",
                table: "subscription_plan_version_benefits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "arabic_description",
                schema: "families",
                table: "subscription_plan_versions");

            migrationBuilder.DropColumn(
                name: "arabic_name",
                schema: "families",
                table: "subscription_plan_versions");

            migrationBuilder.DropColumn(
                name: "english_description",
                schema: "families",
                table: "subscription_plan_versions");

            migrationBuilder.DropColumn(
                name: "english_name",
                schema: "families",
                table: "subscription_plan_versions");

            migrationBuilder.DropColumn(
                name: "arabic_label",
                schema: "families",
                table: "subscription_plan_version_benefits");

            migrationBuilder.DropColumn(
                name: "english_label",
                schema: "families",
                table: "subscription_plan_version_benefits");
        }
    }
}
