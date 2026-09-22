using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTaxRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_tax_rules",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_percentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_tax_rules", x => x.id);
                    table.CheckConstraint("ck_subscription_tax_rules_rate_percentage", "\"rate_percentage\" BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_subscription_tax_rules_version_positive", "\"version\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_subscription_tax_rules_active",
                schema: "families",
                table: "subscription_tax_rules",
                column: "is_active",
                unique: true,
                filter: "\"is_active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ux_subscription_tax_rules_version",
                schema: "families",
                table: "subscription_tax_rules",
                column: "version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_tax_rules",
                schema: "families");
        }
    }
}
