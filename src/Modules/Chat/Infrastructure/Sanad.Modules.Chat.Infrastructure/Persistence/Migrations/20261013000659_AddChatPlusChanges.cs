using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPlusChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "chat",
                table: "Reports",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ChangeRevision",
                schema: "chat",
                table: "Conversations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ChatChanges",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatChanges_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "chat",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatChanges_Messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "chat",
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatOutbox",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatOutbox_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "chat",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatPrivacySettings",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShowOnlineStatus = table.Column<bool>(type: "boolean", nullable: false),
                    ShareLastSeen = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPrivacySettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_AttachmentId",
                schema: "chat",
                table: "Messages",
                column: "AttachmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_ConversationId_MessageId_Type",
                schema: "chat",
                table: "ChatChanges",
                columns: new[] { "ConversationId", "MessageId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_ConversationId_Revision",
                schema: "chat",
                table: "ChatChanges",
                columns: new[] { "ConversationId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_PublishedOnUtc_ConversationId_Revision",
                schema: "chat",
                table: "ChatChanges",
                columns: new[] { "PublishedOnUtc", "ConversationId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_MessageId",
                schema: "chat",
                table: "ChatChanges",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatOutbox_ConversationId",
                schema: "chat",
                table: "ChatOutbox",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatOutbox_MessageId_RecipientUserId",
                schema: "chat",
                table: "ChatOutbox",
                columns: new[] { "MessageId", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatOutbox_PublishedOnUtc_NextAttemptOnUtc",
                schema: "chat",
                table: "ChatOutbox",
                columns: new[] { "PublishedOnUtc", "NextAttemptOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPrivacySettings_OwnerUserId",
                schema: "chat",
                table: "ChatPrivacySettings",
                column: "OwnerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatChanges",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ChatOutbox",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ChatPrivacySettings",
                schema: "chat");

            migrationBuilder.DropIndex(
                name: "IX_Messages_AttachmentId",
                schema: "chat",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "chat",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "ChangeRevision",
                schema: "chat",
                table: "Conversations");
        }
    }
}
