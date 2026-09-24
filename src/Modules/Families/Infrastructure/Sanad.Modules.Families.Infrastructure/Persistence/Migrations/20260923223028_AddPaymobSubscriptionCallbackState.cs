using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymobSubscriptionCallbackState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "paymob_initial_transaction_id",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paymob_subscription_id",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paymob_last_callback_key",
                schema: "families",
                table: "family_subscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paymob_next_billing_on_utc",
                schema: "families",
                table: "family_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paymob_subscription_id",
                schema: "families",
                table: "family_subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paymob_subscription_state",
                schema: "families",
                table: "family_subscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paymob_initial_transaction_id",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "paymob_subscription_id",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "paymob_last_callback_key",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "paymob_next_billing_on_utc",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "paymob_subscription_id",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "paymob_subscription_state",
                schema: "families",
                table: "family_subscriptions");
        }
    }
}
