using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyMedifcalProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elderly_medical_profiles",
                schema: "families",
                columns: table => new
                {
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blood_type = table.Column<int>(type: "integer", nullable: false),
                    height_cm = table.Column<int>(type: "integer", nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    chronic_conditions = table.Column<string>(type: "text", nullable: false),
                    allergies = table.Column<string>(type: "text", nullable: false),
                    medical_history = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_medical_profiles", x => x.elderly_id);
                    table.ForeignKey(
                        name: "FK_elderly_medical_profiles_elderlies_elderly_id",
                        column: x => x.elderly_id,
                        principalSchema: "families",
                        principalTable: "elderlies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elderly_medical_profiles",
                schema: "families");
        }
    }
}
