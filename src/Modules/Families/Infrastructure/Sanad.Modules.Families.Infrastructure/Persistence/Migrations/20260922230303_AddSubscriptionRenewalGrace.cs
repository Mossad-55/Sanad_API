using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionRenewalGrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_renewal_failed_on_utc",
                schema: "families",
                table: "family_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "renewal_grace_ends_on_utc",
                schema: "families",
                table: "family_subscriptions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_renewal_failed_on_utc",
                schema: "families",
                table: "family_subscriptions");

            migrationBuilder.DropColumn(
                name: "renewal_grace_ends_on_utc",
                schema: "families",
                table: "family_subscriptions");
        }
    }
}
