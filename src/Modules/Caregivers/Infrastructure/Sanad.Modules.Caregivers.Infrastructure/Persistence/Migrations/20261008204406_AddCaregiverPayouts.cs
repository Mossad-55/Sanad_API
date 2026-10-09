using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caregiver_payouts",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gross_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    platform_fee_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    charge_rule_version = table.Column<int>(type: "integer", nullable: true),
                    bank_code = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    masked_iban = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    transfer_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    evidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    failed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    failed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_payouts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_caregiver_payouts_caregiver",
                schema: "caregivers",
                table: "caregiver_payouts",
                column: "caregiver_id");

            migrationBuilder.CreateIndex(
                name: "ux_caregiver_payouts_booking_paid",
                schema: "caregivers",
                table: "caregiver_payouts",
                column: "booking_id",
                unique: true,
                filter: "\"status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_payouts",
                schema: "caregivers");
        }
    }
}
