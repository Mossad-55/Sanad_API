using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elderly_check_ins",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    elderly_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    answer = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at_local_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    answered_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    answered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elderly_check_ins", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elderly_check_ins_elderly_id_local_date",
                schema: "families",
                table: "elderly_check_ins",
                columns: new[] { "elderly_id", "local_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elderly_check_ins_local_date",
                schema: "families",
                table: "elderly_check_ins",
                column: "local_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elderly_check_ins",
                schema: "families");
        }
    }
}
