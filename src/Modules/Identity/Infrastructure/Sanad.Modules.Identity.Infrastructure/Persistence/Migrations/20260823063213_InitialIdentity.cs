using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "device_sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    refresh_token_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    rotation_count = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_rotated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reuse_detected_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "social_authentication_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    verified_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    existing_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link_verification_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                    challenge_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    verified_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    arabic_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    phone_verification_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_registration_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    arabic_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    english_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<int>(type: "integer", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    phone_verified = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "verification_requests",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    otp_hash = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    verified_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invalidated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_accounts_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_external_logins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    linked_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "user_identity_documents",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    front_image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    back_image_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    verification_status = table.Column<int>(type: "integer", nullable: false),
                    review_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_identity_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_identity_documents_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_expires_on_utc",
                schema: "identity",
                table: "device_sessions",
                column: "expires_on_utc");

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_refresh_token_hash",
                schema: "identity",
                table: "device_sessions",
                column: "refresh_token_hash");

            migrationBuilder.CreateIndex(
                name: "IX_device_sessions_user_id_revoked_on_utc_expires_on_utc",
                schema: "identity",
                table: "device_sessions",
                columns: new[] { "user_id", "revoked_on_utc", "expires_on_utc" });

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
                name: "IX_user_accounts_user_id_account_type",
                schema: "identity",
                table: "user_accounts",
                columns: new[] { "user_id", "account_type" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_user_identity_documents_user_id",
                schema: "identity",
                table: "user_identity_documents",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true,
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_phone_number",
                schema: "identity",
                table: "users",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_status",
                schema: "identity",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_verification_requests_expires_on_utc",
                schema: "identity",
                table: "verification_requests",
                column: "expires_on_utc");

            migrationBuilder.CreateIndex(
                name: "IX_verification_requests_target_purpose_status",
                schema: "identity",
                table: "verification_requests",
                columns: new[] { "target", "purpose", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_verification_requests_user_id_purpose_status",
                schema: "identity",
                table: "verification_requests",
                columns: new[] { "user_id", "purpose", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "social_authentication_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "social_registration_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_accounts",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_external_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_identity_documents",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "verification_requests",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");
        }
    }
}
