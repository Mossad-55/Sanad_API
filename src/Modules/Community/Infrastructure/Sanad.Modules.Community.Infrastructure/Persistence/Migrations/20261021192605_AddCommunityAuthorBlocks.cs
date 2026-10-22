using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Community.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityAuthorBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommunityAuthorBlocks",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedAuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityAuthorBlocks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAuthorBlocks_BlockerUserId",
                schema: "community",
                table: "CommunityAuthorBlocks",
                column: "BlockerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAuthorBlocks_BlockerUserId_BlockedAuthorUserId",
                schema: "community",
                table: "CommunityAuthorBlocks",
                columns: new[] { "BlockerUserId", "BlockedAuthorUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityAuthorBlocks",
                schema: "community");
        }
    }
}
