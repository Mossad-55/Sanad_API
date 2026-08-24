using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAuthenticationNonces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_authentication_nonces",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    nonce_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_authentication_nonces", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_authentication_nonces_nonce_hash",
                schema: "identity",
                table: "external_authentication_nonces",
                column: "nonce_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_authentication_nonces_provider_expires_on_utc_cons~",
                schema: "identity",
                table: "external_authentication_nonces",
                columns: new[] { "provider", "expires_on_utc", "consumed_on_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_authentication_nonces",
                schema: "identity");
        }
    }
}
