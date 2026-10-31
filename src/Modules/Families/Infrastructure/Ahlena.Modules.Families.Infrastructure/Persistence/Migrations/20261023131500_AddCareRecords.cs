using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "care_records",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_family_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_facility_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_caregiver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hospital_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    visit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_care_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_care_records_elderlies_elderly_id",
                        column: x => x.elderly_id,
                        principalSchema: "families",
                        principalTable: "elderlies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "care_record_audits",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_care_record_audits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_care_records_author_facility_id",
                schema: "families",
                table: "care_records",
                column: "author_facility_id");

            migrationBuilder.CreateIndex(
                name: "IX_care_records_elderly_id",
                schema: "families",
                table: "care_records",
                column: "elderly_id");

            migrationBuilder.CreateIndex(
                name: "IX_care_records_elderly_id_source",
                schema: "families",
                table: "care_records",
                columns: new[] { "elderly_id", "source" });

            migrationBuilder.CreateIndex(
                name: "IX_care_records_visit_date",
                schema: "families",
                table: "care_records",
                column: "visit_date");

            migrationBuilder.CreateIndex(
                name: "IX_care_record_audits_actor_user_id_occurred_on_utc",
                schema: "families",
                table: "care_record_audits",
                columns: new[] { "actor_user_id", "occurred_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_care_record_audits_elderly_id_occurred_on_utc",
                schema: "families",
                table: "care_record_audits",
                columns: new[] { "elderly_id", "occurred_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_care_record_audits_occurred_on_utc",
                schema: "families",
                table: "care_record_audits",
                column: "occurred_on_utc");

            migrationBuilder.CreateIndex(
                name: "IX_care_record_audits_record_id",
                schema: "families",
                table: "care_record_audits",
                column: "record_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_records",
                schema: "families");

            migrationBuilder.DropTable(
                name: "care_record_audits",
                schema: "families");
        }
    }
}
