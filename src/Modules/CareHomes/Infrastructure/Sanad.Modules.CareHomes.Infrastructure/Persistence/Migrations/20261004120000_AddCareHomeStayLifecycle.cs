using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CareHomesDbContext))]
[Migration("20261004120000_AddCareHomeStayLifecycle")]
public partial class AddCareHomeStayLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "assigned_room_id", table: "bookings", schema: "care_homes", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "assigned_bed_id", table: "bookings", schema: "care_homes", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "actual_check_in_on_utc", table: "bookings", schema: "care_homes", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "actual_check_out_on_utc", table: "bookings", schema: "care_homes", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "actual_check_in_recorded_by", table: "bookings", schema: "care_homes", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "actual_check_out_recorded_by", table: "bookings", schema: "care_homes", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "family_check_in_confirmed_on_utc", table: "bookings", schema: "care_homes", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "family_check_in_confirmed_by", table: "bookings", schema: "care_homes", nullable: true);

        migrationBuilder.CreateTable(
            name: "check_in_disputes",
            schema: "care_homes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                CheckoutOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OpenedBy = table.Column<Guid>(type: "uuid", nullable: false),
                OpenedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ResolvedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EffectiveCheckInOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            }, constraints: table => table.PrimaryKey("PK_check_in_disputes", x => x.id));

        migrationBuilder.CreateIndex("IX_check_in_disputes_BookingId_Status", "check_in_disputes", new[] { "BookingId", "Status" }, "care_homes");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("check_in_disputes", "care_homes");
        migrationBuilder.DropColumn("assigned_room_id", "bookings", "care_homes");
        migrationBuilder.DropColumn("assigned_bed_id", "bookings", "care_homes");
        migrationBuilder.DropColumn("actual_check_in_on_utc", "bookings", "care_homes");
        migrationBuilder.DropColumn("actual_check_out_on_utc", "bookings", "care_homes");
        migrationBuilder.DropColumn("actual_check_in_recorded_by", "bookings", "care_homes");
        migrationBuilder.DropColumn("actual_check_out_recorded_by", "bookings", "care_homes");
        migrationBuilder.DropColumn("family_check_in_confirmed_on_utc", "bookings", "care_homes");
        migrationBuilder.DropColumn("family_check_in_confirmed_by", "bookings", "care_homes");
    }
}
