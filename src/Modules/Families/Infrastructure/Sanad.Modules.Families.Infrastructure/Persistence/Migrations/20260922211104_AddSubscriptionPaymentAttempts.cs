using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_payment_attempts",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_rate_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_payable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    coupon_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    paymob_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paymob_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    settled_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payment_attempts", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscription_payment_attempts_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "families",
                        principalTable: "families",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_payment_attempts_subscription_plan_versions_pl~",
                        column: x => x.plan_version_id,
                        principalSchema: "families",
                        principalTable: "subscription_plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_attempts_family_id_status",
                schema: "families",
                table: "subscription_payment_attempts",
                columns: new[] { "family_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_attempts_paymob_order_id",
                schema: "families",
                table: "subscription_payment_attempts",
                column: "paymob_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_payment_attempts_plan_version_id",
                schema: "families",
                table: "subscription_payment_attempts",
                column: "plan_version_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_payment_attempts",
                schema: "families");
        }
    }
}
