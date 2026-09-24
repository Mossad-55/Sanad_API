using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymobSubscriptionIdentityRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paymob_subscription_identities",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_subscription_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paymob_subscription_identities", x => x.id);
                    table.ForeignKey(
                        name: "FK_paymob_subscription_identities_family_subscriptions_family_~",
                        column: x => x.family_subscription_id,
                        principalSchema: "families",
                        principalTable: "family_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_paymob_subscription_identities_subscription_payment_attempt~",
                        column: x => x.payment_attempt_id,
                        principalSchema: "families",
                        principalTable: "subscription_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_subscription_payment_attempts_paymob_subscription_id",
                schema: "families",
                table: "subscription_payment_attempts",
                column: "paymob_subscription_id",
                unique: true,
                filter: "paymob_subscription_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_family_subscriptions_paymob_subscription_id",
                schema: "families",
                table: "family_subscriptions",
                column: "paymob_subscription_id",
                unique: true,
                filter: "paymob_subscription_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_paymob_subscription_identities_family_subscription_id",
                schema: "families",
                table: "paymob_subscription_identities",
                column: "family_subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_paymob_subscription_identities_payment_attempt_id",
                schema: "families",
                table: "paymob_subscription_identities",
                column: "payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ux_paymob_subscription_identities_provider_subscription_id",
                schema: "families",
                table: "paymob_subscription_identities",
                column: "provider_subscription_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paymob_subscription_identities",
                schema: "families");

            migrationBuilder.DropIndex(
                name: "ux_subscription_payment_attempts_paymob_subscription_id",
                schema: "families",
                table: "subscription_payment_attempts");

            migrationBuilder.DropIndex(
                name: "ux_family_subscriptions_paymob_subscription_id",
                schema: "families",
                table: "family_subscriptions");
        }
    }
}
