using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caregiver certificate lifecycle: an append-only per-qualification upload
    /// history plus logical (never physical) removal.
    ///
    /// This migration is NON-DESTRUCTIVE. It adds three columns to the existing
    /// <c>caregivers.caregiver_certificates</c> row (a whole-qualification
    /// tombstone flag, its instant, and an optimistic-concurrency revision) and
    /// creates <c>caregivers.caregiver_certificate_versions</c>, the per-upload
    /// history child. Existing certificate rows keep their id and data: no
    /// column is dropped, renamed or rewritten, and every historical file key is
    /// preserved. A partial unique index enforces "at most one current version
    /// per qualification" while allowing any number of removed history rows.
    /// </summary>
    public partial class AddCaregiverCertificateVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_removed",
                schema: "caregivers",
                table: "caregiver_certificates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "removed_on_utc",
                schema: "caregivers",
                table: "caregiver_certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "caregivers",
                table: "caregiver_certificates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "caregiver_certificate_versions",
                schema: "caregivers",
                columns: table => new
                {
                    caregiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    certificate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_path = table.Column<string>(type: "text", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<int>(type: "integer", nullable: false),
                    review_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    superseded_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    removed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    removed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caregiver_certificate_versions", x => new { x.caregiver_id, x.certificate_id, x.id });
                    table.ForeignKey(
                        name: "FK_caregiver_certificate_versions_caregiver_certificates_careg~",
                        columns: x => new { x.caregiver_id, x.certificate_id },
                        principalSchema: "caregivers",
                        principalTable: "caregiver_certificates",
                        principalColumns: new[] { "caregiver_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_caregiver_certificate_versions_current",
                schema: "caregivers",
                table: "caregiver_certificate_versions",
                columns: new[] { "caregiver_id", "certificate_id" },
                unique: true,
                filter: "is_current AND NOT is_removed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_caregiver_certificate_versions_current",
                schema: "caregivers",
                table: "caregiver_certificate_versions");

            migrationBuilder.DropTable(
                name: "caregiver_certificate_versions",
                schema: "caregivers");

            migrationBuilder.DropColumn(
                name: "is_removed",
                schema: "caregivers",
                table: "caregiver_certificates");

            migrationBuilder.DropColumn(
                name: "removed_on_utc",
                schema: "caregivers",
                table: "caregiver_certificates");

            migrationBuilder.DropColumn(
                name: "revision",
                schema: "caregivers",
                table: "caregiver_certificates");
        }
    }
}
