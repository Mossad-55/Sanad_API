using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddElderlyCheckInElderlyForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_elderly_check_ins_elderlies_elderly_id",
                schema: "families",
                table: "elderly_check_ins",
                column: "elderly_id",
                principalSchema: "families",
                principalTable: "elderlies",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_elderly_check_ins_elderlies_elderly_id",
                schema: "families",
                table: "elderly_check_ins");
        }
    }
}
