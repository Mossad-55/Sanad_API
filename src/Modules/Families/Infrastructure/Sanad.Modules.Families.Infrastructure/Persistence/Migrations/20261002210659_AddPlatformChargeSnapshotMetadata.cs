using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformChargeSnapshotMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "platform_charge_rule_version",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "platform_fee_amount",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "platform_fee_rate_percentage",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "renewal_provider_event_id",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "platform_charge_rule_version",
                schema: "families",
                table: "subscription_invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "platform_fee_amount",
                schema: "families",
                table: "subscription_invoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "platform_fee_rate_percentage",
                schema: "families",
                table: "subscription_invoices",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_period_base_amount",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "current_period_platform_charge_rule_version",
                schema: "families",
                table: "family_subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_period_platform_fee_amount",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_period_platform_fee_rate_percentage",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "current_period_tax_amount",
                schema: "families",
                table: "family_subscriptions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "price_platform_charge_rule_version",
                schema: "families",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "price_tax_amount",
                schema: "families",
                table: "bookings",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "price_tax_rate_percentage",
                schema: "families",
                table: "bookings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_attempts_renewal_provider_event_id",
                schema: "families",
                table: "subscription_payment_attempts",
                column: "renewal_provider_event_id",
                unique: true,
                filter: "renewal_provider_event_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscription_payment_attempts_renewal_provider_event_id",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "platform_charge_rule_version",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "platform_fee_amount",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "platform_fee_rate_percentage",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "renewal_provider_event_id",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "platform_charge_rule_version",
                schema: "families",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "platform_fee_amount",
                schema: "families",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "platform_fee_rate_percentage",
                schema: "families",
                table: "subscription_invoices");

            migrationBuilder.DropColumn(
                name: "current_period_base_amount",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_period_platform_charge_rule_version",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_period_platform_fee_amount",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_period_platform_fee_rate_percentage",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "current_period_tax_amount",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "price_platform_charge_rule_version",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "price_tax_amount",
                schema: "families",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "price_tax_rate_percentage",
                schema: "families",
                table: "bookings");
        }
    }
}
