using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSplashAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_splash_screens_audience_status_display_order",
                schema: "cms",
                table: "splash_screens");

            migrationBuilder.DropColumn(
                name: "audience",
                schema: "cms",
                table: "splash_screens");

            migrationBuilder.CreateIndex(
                name: "IX_splash_screens_status_display_order",
                schema: "cms",
                table: "splash_screens",
                columns: new[] { "status", "display_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_splash_screens_status_display_order",
                schema: "cms",
                table: "splash_screens");

            migrationBuilder.AddColumn<int>(
                name: "audience",
                schema: "cms",
                table: "splash_screens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_splash_screens_audience_status_display_order",
                schema: "cms",
                table: "splash_screens",
                columns: new[] { "audience", "status", "display_order" });
        }
    }
}
