using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Community.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RejectedBy",
                schema: "community",
                table: "Posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedOnUtc",
                schema: "community",
                table: "Posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionNote",
                schema: "community",
                table: "Posts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasonCode",
                schema: "community",
                table: "Posts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommunityAuthorRestrictions",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModeratorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Duration = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartsOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityAuthorRestrictions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommunityPostReports",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityPostReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityPostReports_Posts_PostId",
                        column: x => x.PostId,
                        principalSchema: "community",
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAuthorRestrictions_AuthorId",
                schema: "community",
                table: "CommunityAuthorRestrictions",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAuthorRestrictions_ExpiresOnUtc",
                schema: "community",
                table: "CommunityAuthorRestrictions",
                column: "ExpiresOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityAuthorRestrictions_RevokedOnUtc",
                schema: "community",
                table: "CommunityAuthorRestrictions",
                column: "RevokedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostReports_PostId_ReporterId",
                schema: "community",
                table: "CommunityPostReports",
                columns: new[] { "PostId", "ReporterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityPostReports_Status",
                schema: "community",
                table: "CommunityPostReports",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityAuthorRestrictions",
                schema: "community");

            migrationBuilder.DropTable(
                name: "CommunityPostReports",
                schema: "community");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "community",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "RejectedOnUtc",
                schema: "community",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "RejectionNote",
                schema: "community",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "RejectionReasonCode",
                schema: "community",
                table: "Posts");
        }
    }
}
