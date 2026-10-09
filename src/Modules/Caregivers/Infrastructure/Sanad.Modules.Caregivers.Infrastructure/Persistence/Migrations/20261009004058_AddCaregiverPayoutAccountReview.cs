using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiverPayoutAccountReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reference",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_on_utc",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "verification_source",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verified_by",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "verified_on_utc",
                schema: "caregivers",
                table: "caregiver_payout_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "caregiver_payout_account_reviews",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payout_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_revision = table.Column<int>(type: "integer", nullable: false),
                    decision = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    verification_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_payout_account_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_caregiver_payout_account_reviews_caregiver_payout_accounts_~",
                        column: x => x.payout_account_id,
                        principalSchema: "caregivers",
                        principalTable: "caregiver_payout_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_caregiver_payout_account_reviews_account",
                schema: "caregivers",
                table: "caregiver_payout_account_reviews",
                column: "payout_account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caregiver_payout_account_reviews",
                schema: "caregivers");

            migrationBuilder.DropColumn(
                name: "reference",
                schema: "caregivers",
                table: "caregiver_payout_accounts");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "caregivers",
                table: "caregiver_payout_accounts");

            migrationBuilder.DropColumn(
                name: "reviewed_on_utc",
                schema: "caregivers",
                table: "caregiver_payout_accounts");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "caregivers",
                table: "caregiver_payout_accounts");

            migrationBuilder.DropColumn(
                name: "verification_source",
                schema: "caregivers",
                table: "caregiver_payout_accounts");

            migrationBuilder.DropColumn(
                name: "verified_by",
                schema: "caregivers",
                table: "caregiver_payout_accounts");

            migrationBuilder.DropColumn(
                name: "verified_on_utc",
                schema: "caregivers",
                table: "caregiver_payout_accounts");
        }
    }
}
