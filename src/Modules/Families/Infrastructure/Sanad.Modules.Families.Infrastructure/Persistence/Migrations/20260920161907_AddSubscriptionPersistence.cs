using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_subscriptions",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    member_limit_kind = table.Column<int>(type: "integer", nullable: false),
                    member_limit_value = table.Column<int>(type: "integer", nullable: true),
                    monthly_booking_limit_kind = table.Column<int>(type: "integer", nullable: false),
                    monthly_booking_limit_value = table.Column<int>(type: "integer", nullable: true),
                    rollover = table.Column<int>(type: "integer", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_family_subscriptions_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "families",
                        principalTable: "families",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plan_versions",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    member_limit_kind = table.Column<int>(type: "integer", nullable: false),
                    member_limit_value = table.Column<int>(type: "integer", nullable: true),
                    monthly_booking_limit_kind = table.Column<int>(type: "integer", nullable: false),
                    monthly_booking_limit_value = table.Column<int>(type: "integer", nullable: true),
                    rollover = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    is_available_for_new_sales = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plan_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "family_subscription_benefits",
                schema: "families",
                columns: table => new
                {
                    benefit_key = table.Column<int>(type: "integer", nullable: false),
                    family_subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_included = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_subscription_benefits", x => new { x.family_subscription_id, x.benefit_key });
                    table.ForeignKey(
                        name: "FK_family_subscription_benefits_family_subscriptions_family_su~",
                        column: x => x.family_subscription_id,
                        principalSchema: "families",
                        principalTable: "family_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plan_version_benefits",
                schema: "families",
                columns: table => new
                {
                    benefit_key = table.Column<int>(type: "integer", nullable: false),
                    subscription_plan_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_included = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plan_version_benefits", x => new { x.subscription_plan_version_id, x.benefit_key });
                    table.ForeignKey(
                        name: "FK_subscription_plan_version_benefits_subscription_plan_versio~",
                        column: x => x.subscription_plan_version_id,
                        principalSchema: "families",
                        principalTable: "subscription_plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_family_subscriptions_current",
                schema: "families",
                table: "family_subscriptions",
                column: "family_id",
                unique: true,
                filter: "is_current = true");

            migrationBuilder.CreateIndex(
                name: "ux_subscription_plan_versions_key_version",
                schema: "families",
                table: "subscription_plan_versions",
                columns: new[] { "plan_key", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_subscription_benefits",
                schema: "families");

            migrationBuilder.DropTable(
                name: "subscription_plan_version_benefits",
                schema: "families");

            migrationBuilder.DropTable(
                name: "family_subscriptions",
                schema: "families");

            migrationBuilder.DropTable(
                name: "subscription_plan_versions",
                schema: "families");
        }
    }
}
