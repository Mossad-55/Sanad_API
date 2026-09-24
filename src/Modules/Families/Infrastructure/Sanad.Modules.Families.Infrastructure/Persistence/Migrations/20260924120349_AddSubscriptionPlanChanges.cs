using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlanChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_plan_change",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "prorated_credit",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "source_period_ends_on_utc",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "source_period_gross",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "source_period_tax_rate_percentage",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_period_gross",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_period_tax_rate_percentage",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_downgrade_currency",
                schema: "families",
                table: "family_subscriptions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_cycle",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_member_limit_kind",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_member_limit_value",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_monthly_booking_limit_kind",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_monthly_booking_limit_value",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_downgrade_plan_key",
                schema: "families",
                table: "family_subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_plan_version",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "pending_downgrade_price",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_downgrade_rollover",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "family_subscription_pending_downgrade_benefits",
                schema: "families",
                columns: table => new
                {
                    benefit_key = table.Column<int>(type: "integer", nullable: false),
                    family_subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_included = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_subscription_pending_downgrade_benefits", x => new { x.family_subscription_id, x.benefit_key });
                    table.ForeignKey(
                        name: "FK_family_subscription_pending_downgrade_benefits_family_subsc~",
                        column: x => x.family_subscription_id,
                        principalSchema: "families",
                        principalTable: "family_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_subscription_pending_downgrade_benefits",
                schema: "families");

            migrationBuilder.DropColumn(
                name: "is_plan_change",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "prorated_credit",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "source_period_ends_on_utc",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "source_period_gross",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "source_period_tax_rate_percentage",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "current_period_gross",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_period_tax_rate_percentage",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_currency",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_cycle",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_member_limit_kind",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_member_limit_value",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_monthly_booking_limit_kind",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_monthly_booking_limit_value",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_plan_key",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_plan_version",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_price",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_downgrade_rollover",
                schema: "families",
                table: "family_subscriptions");
        }
    }
}
