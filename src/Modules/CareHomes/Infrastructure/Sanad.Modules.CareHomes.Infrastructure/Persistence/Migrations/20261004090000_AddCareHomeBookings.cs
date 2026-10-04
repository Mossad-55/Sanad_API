using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations;

public partial class AddCareHomeBookings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "bookings",
            schema: "care_homes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                FamilyUserId = table.Column<Guid>(type: "uuid", nullable: false),
                FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                ElderlyId = table.Column<Guid>(type: "uuid", nullable: false),
                RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                BaseAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                PlatformFeeAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                ChargeRuleVersion = table.Column<int>(type: "integer", nullable: false),
                ElderlyArabicName = table.Column<string>(type: "text", nullable: false),
                ElderlyEnglishName = table.Column<string>(type: "text", nullable: false),
                ElderlyAge = table.Column<int>(type: "integer", nullable: false),
                MedicalSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                ResponsibleContactName = table.Column<string>(type: "text", nullable: false),
                ResponsibleContactPhone = table.Column<string>(type: "text", nullable: true),
                ResponsibleContactRelationship = table.Column<string>(type: "text", nullable: true),
                CareNeedsNotes = table.Column<string>(type: "text", nullable: true),
                MerchantReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                PaymobTransactionId = table.Column<long>(type: "bigint", nullable: true),
                RefundReference = table.Column<string>(type: "text", nullable: true),
                refund_claimed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CheckoutHoldUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DecisionHoldUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DecidedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DecisionReason = table.Column<string>(type: "text", nullable: true),
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                , earliest_arrival_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                , payment_intent_claimed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                , payment_intent_method = table.Column<int>(type: "integer", nullable: true)
                , paymob_order_id = table.Column<string>(type: "text", nullable: true)
                , intention_order_id = table.Column<string>(type: "text", nullable: true)
                , payment_client_secret = table.Column<string>(type: "text", nullable: true)
                , payment_public_key = table.Column<string>(type: "text", nullable: true)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_bookings", x => x.id);
                table.ForeignKey("FK_bookings_facilities_facility_id", x => x.FacilityId, "facilities", "id", principalSchema: "care_homes", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_bookings_MerchantReference", "bookings", "MerchantReference", "care_homes", unique: true);
        migrationBuilder.CreateIndex("IX_bookings_facility_status_dates", "bookings", new[] { "FacilityId", "Status", "StartDate", "EndDate" }, "care_homes");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("bookings", "care_homes");
}
