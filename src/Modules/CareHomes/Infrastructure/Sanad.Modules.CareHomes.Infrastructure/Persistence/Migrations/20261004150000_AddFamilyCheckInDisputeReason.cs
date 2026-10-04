using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CareHomesDbContext))]
[Migration("20261004150000_AddFamilyCheckInDisputeReason")]
public partial class AddFamilyCheckInDisputeReason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateTime>(
            name: "CheckoutOnUtc",
            table: "check_in_disputes",
            schema: "care_homes",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "timestamp with time zone");

        migrationBuilder.AddColumn<string>(
            name: "FamilyReason",
            table: "check_in_disputes",
            schema: "care_homes",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.DropIndex(
            name: "IX_check_in_disputes_BookingId_Status",
            table: "check_in_disputes",
            schema: "care_homes");

        migrationBuilder.CreateIndex(
            name: "IX_check_in_disputes_BookingId_Status",
            table: "check_in_disputes",
            schema: "care_homes",
            columns: new[] { "BookingId", "Status" },
            unique: true,
            filter: "\"Status\" = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE care_homes.check_in_disputes SET \"CheckoutOnUtc\" = \"OpenedOnUtc\" WHERE \"CheckoutOnUtc\" IS NULL;");
        migrationBuilder.DropIndex("IX_check_in_disputes_BookingId_Status", "check_in_disputes", "care_homes");
        migrationBuilder.CreateIndex("IX_check_in_disputes_BookingId_Status", "check_in_disputes", new[] { "BookingId", "Status" }, "care_homes");
        migrationBuilder.DropColumn("FamilyReason", "check_in_disputes", "care_homes");
        migrationBuilder.AlterColumn<DateTime>(name: "CheckoutOnUtc", table: "check_in_disputes", schema: "care_homes", type: "timestamp with time zone", nullable: false, oldClrType: typeof(DateTime?), oldType: "timestamp with time zone");
    }
}
