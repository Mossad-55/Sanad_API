using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerContactOptionalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "identity",
                table: "partner_contacts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_type",
                schema: "identity",
                table: "partner_contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "identity",
                table: "partner_contacts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "city",
                schema: "identity",
                table: "partner_contacts");

            migrationBuilder.DropColumn(
                name: "company_type",
                schema: "identity",
                table: "partner_contacts");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "identity",
                table: "partner_contacts");
        }
    }
}
