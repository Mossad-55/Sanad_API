using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymobSubscriptionCallbackLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paymob_subscription_callbacks",
                schema: "families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    callback_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    paymob_request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paymob_subscription_callbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_paymob_subscription_callbacks_key",
                schema: "families",
                table: "paymob_subscription_callbacks",
                column: "callback_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_paymob_subscription_callbacks_request_id",
                schema: "families",
                table: "paymob_subscription_callbacks",
                column: "paymob_request_id",
                unique: true,
                filter: "paymob_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paymob_subscription_callbacks",
                schema: "families");
        }
    }
}
