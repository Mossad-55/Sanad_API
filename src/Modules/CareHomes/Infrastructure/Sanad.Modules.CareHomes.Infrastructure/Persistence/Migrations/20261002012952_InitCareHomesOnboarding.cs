using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.CareHomes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitCareHomesOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "care_homes");

            migrationBuilder.CreateTable(
                name: "facilities",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    submitted_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facilities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    profile_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    private_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    length = table.Column<long>(type: "bigint", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    verified_non_expiring = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_documents_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile_revisions",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    arabic_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    arabic_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    english_description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    contact_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    governorate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    arabic_admission_conditions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    english_admission_conditions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    amenities_json = table.Column<string>(type: "text", nullable: false),
                    medical_services_json = table.Column<string>(type: "text", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_profile_revisions_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_history",
                schema: "care_homes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    previous_status = table.Column<int>(type: "integer", nullable: false),
                    new_status = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    care_home_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_review_history_facilities_care_home_id",
                        column: x => x.care_home_id,
                        principalSchema: "care_homes",
                        principalTable: "facilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_care_home_id_profile_revision_id_type",
                schema: "care_homes",
                table: "documents",
                columns: new[] { "care_home_id", "profile_revision_id", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_facilities_owner_user_id",
                schema: "care_homes",
                table: "facilities",
                column: "owner_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_revisions_care_home_id_revision_number",
                schema: "care_homes",
                table: "profile_revisions",
                columns: new[] { "care_home_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_review_history_care_home_id",
                schema: "care_homes",
                table: "review_history",
                column: "care_home_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "profile_revisions",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "review_history",
                schema: "care_homes");

            migrationBuilder.DropTable(
                name: "facilities",
                schema: "care_homes");
        }
    }
}
