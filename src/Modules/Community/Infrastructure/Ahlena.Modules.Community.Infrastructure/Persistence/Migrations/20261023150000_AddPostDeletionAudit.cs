using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ahlena.Modules.Community.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostDeletionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive only: the deletion transition reuses the existing
            // Posts.Status column (new value 5); this migration adds the
            // deletion audit attributes and the lifecycle concurrency token.
            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "community",
                table: "Posts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                schema: "community",
                table: "Posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedOnUtc",
                schema: "community",
                table: "Posts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                schema: "community",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                schema: "community",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "DeletedOnUtc",
                schema: "community",
                table: "Posts");
        }
    }
}
