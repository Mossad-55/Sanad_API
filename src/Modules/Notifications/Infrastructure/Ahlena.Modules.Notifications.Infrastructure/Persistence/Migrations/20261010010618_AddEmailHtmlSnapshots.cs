using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailHtmlSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "html_body",
                schema: "notifications",
                table: "email_outbox",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "html_body",
                schema: "notifications",
                table: "email_outbox");
        }
    }
}
