using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSocialAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_authentication_nonces",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "social_authentication_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "social_registration_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_external_logins",
                schema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_authentication_nonces",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    nonce_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_authentication_nonces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "social_authentication_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    consumed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    email_is_authoritative = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    existing_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    link_verification_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    verified_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_authentication_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "social_registration_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    arabic_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    challenge_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    consumed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    english_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    phone_verification_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    verified_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_registration_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_external_logins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_external_logins", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_external_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "IX_social_authentication_challenges_challenge_hash",
                schema: "identity",
                table: "social_authentication_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_social_authentication_challenges_expires_on_utc_consumed_on~",
                schema: "identity",
                table: "social_authentication_challenges",
                columns: new[] { "expires_on_utc", "consumed_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_social_registration_challenges_challenge_hash",
                schema: "identity",
                table: "social_registration_challenges",
                column: "challenge_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_social_registration_challenges_expires_on_utc_consumed_on_u~",
                schema: "identity",
                table: "social_registration_challenges",
                columns: new[] { "expires_on_utc", "consumed_on_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_user_external_logins_provider_provider_subject",
                schema: "identity",
                table: "user_external_logins",
                columns: new[] { "provider", "provider_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_external_logins_user_id_provider",
                schema: "identity",
                table: "user_external_logins",
                columns: new[] { "user_id", "provider" },
                unique: true);
        }
    }
}
