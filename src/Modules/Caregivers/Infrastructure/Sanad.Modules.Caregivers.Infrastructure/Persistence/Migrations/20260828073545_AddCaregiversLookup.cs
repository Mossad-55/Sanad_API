using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaregiversLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "caregivers");

            migrationBuilder.CreateTable(
                name: "academic_degrees",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_academic_degrees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "areas",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_areas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cities",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    governorate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "governorates",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_governorates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "languages",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    english_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_languages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "professional_titles",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_titles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    caregiver_type = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    icon_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "specializations",
                schema: "caregivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    caregiver_type = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_specializations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_areas_city_id",
                schema: "caregivers",
                table: "areas",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "IX_cities_governorate_id",
                schema: "caregivers",
                table: "cities",
                column: "governorate_id");

            migrationBuilder.CreateIndex(
                name: "IX_languages_code",
                schema: "caregivers",
                table: "languages",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_services_caregiver_type",
                schema: "caregivers",
                table: "services",
                column: "caregiver_type");

            migrationBuilder.CreateIndex(
                name: "IX_specializations_caregiver_type",
                schema: "caregivers",
                table: "specializations",
                column: "caregiver_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "academic_degrees",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "areas",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "cities",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "governorates",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "languages",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "professional_titles",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "services",
                schema: "caregivers");

            migrationBuilder.DropTable(
                name: "specializations",
                schema: "caregivers");
        }
    }
}
