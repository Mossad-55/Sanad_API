using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Chat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "chat");

            migrationBuilder.CreateTable(
                name: "Blocks",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantOne = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantTwo = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: true),
                    HiddenForParticipantOne = table.Column<bool>(type: "boolean", nullable: false),
                    HiddenForParticipantTwo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsBound = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "chat",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationReadStates",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastReadMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationReadStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationReadStates_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "chat",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ConversationId",
                schema: "chat",
                table: "Attachments",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_StorageKey",
                schema: "chat",
                table: "Attachments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_BlockerUserId",
                schema: "chat",
                table: "Blocks",
                column: "BlockerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_BlockerUserId_BlockedUserId",
                schema: "chat",
                table: "Blocks",
                columns: new[] { "BlockerUserId", "BlockedUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReadStates_ConversationId_UserId",
                schema: "chat",
                table: "ConversationReadStates",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_LastActivityOnUtc",
                schema: "chat",
                table: "Conversations",
                column: "LastActivityOnUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ParticipantOne_ParticipantTwo_Scope_BookingId",
                schema: "chat",
                table: "Conversations",
                columns: new[] { "ParticipantOne", "ParticipantTwo", "Scope", "BookingId" },
                unique: true,
                filter: "\"BookingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ParticipantOne_ParticipantTwo_Scope_Direct",
                schema: "chat",
                table: "Conversations",
                columns: new[] { "ParticipantOne", "ParticipantTwo", "Scope" },
                unique: true,
                filter: "\"BookingId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "Blocks",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "ConversationReadStates",
                schema: "chat");

            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "chat");
        }
    }
}
