using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_on_utc",
                schema: "families",
                table: "families",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deletion_message",
                schema: "families",
                table: "families",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deletion_reason",
                schema: "families",
                table: "families",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_on_utc",
                schema: "families",
                table: "families");

            migrationBuilder.DropColumn(
                name: "deletion_message",
                schema: "families",
                table: "families");

            migrationBuilder.DropColumn(
                name: "deletion_reason",
                schema: "families",
                table: "families");
        }
    }
}
