using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentCleanupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Attachments_IsBound_UploadedOnUtc",
                schema: "chat",
                table: "Attachments",
                columns: new[] { "IsBound", "UploadedOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attachments_IsBound_UploadedOnUtc",
                schema: "chat",
                table: "Attachments");
        }
    }
}
