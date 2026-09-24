using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_invoices",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_payable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    period_starts_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_ends_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    issued_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    pdf_storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_invoices", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscription_invoices_family_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "families",
                        principalTable: "family_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_subscription_invoices_subscription_payment_attempts_payment~",
                        column: x => x.payment_attempt_id,
                        principalSchema: "families",
                        principalTable: "subscription_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_invoices_invoice_number",
                schema: "families",
                table: "subscription_invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscription_invoices_payment_attempt_id",
                schema: "families",
                table: "subscription_invoices",
                column: "payment_attempt_id",
                unique: true,
                filter: "payment_attempt_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_invoices_provider_event_id",
                schema: "families",
                table: "subscription_invoices",
                column: "provider_event_id",
                unique: true,
                filter: "provider_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_invoices_subscription_id",
                schema: "families",
                table: "subscription_invoices",
                column: "subscription_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_invoices",
                schema: "families");
        }
    }
}
