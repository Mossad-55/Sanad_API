using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionRenewalAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_renewal",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "subscription_id",
                schema: "families",
                table: "subscription_payment_attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_attempts_subscription_id_is_renewal_st~",
                schema: "families",
                table: "subscription_payment_attempts",
                columns: new[] { "subscription_id", "is_renewal", "status" });

            migrationBuilder.AddForeignKey(
                name: "FK_subscription_payment_attempts_family_subscriptions_subscrip~",
                schema: "families",
                table: "subscription_payment_attempts",
                column: "subscription_id",
                principalSchema: "families",
                principalTable: "family_subscriptions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscription_payment_attempts_family_subscriptions_subscrip~",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropIndex(
                name: "IX_subscription_payment_attempts_subscription_id_is_renewal_st~",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "is_renewal",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropColumn(
                name: "subscription_id",
                schema: "families",
                table: "subscription_payment_attempts");
        }
    }
}
