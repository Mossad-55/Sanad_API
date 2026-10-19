using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddE04PlatformControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_control_audit_entries",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_version = table.Column<int>(type: "integer", nullable: false),
                    new_version = table.Column<int>(type: "integer", nullable: false),
                    before_state = table.Column<int>(type: "integer", nullable: false),
                    after_state = table.Column<int>(type: "integer", nullable: false),
                    before_maintenance_message_arabic = table.Column<string>(type: "text", nullable: true),
                    after_maintenance_message_arabic = table.Column<string>(type: "text", nullable: true),
                    before_maintenance_message_english = table.Column<string>(type: "text", nullable: true),
                    after_maintenance_message_english = table.Column<string>(type: "text", nullable: true),
                    before_card_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    after_card_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    before_wallet_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    after_wallet_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    internal_reason = table.Column<string>(type: "text", nullable: true),
                    occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_control_audit_entries", x => x.id);
                    table.CheckConstraint("ck_platform_control_audit_entries_states", "before_state IN (1, 2) AND after_state IN (1, 2)");
                    table.CheckConstraint("ck_platform_control_audit_entries_versions", "previous_version > 0 AND new_version = previous_version + 1");
                });

            migrationBuilder.CreateTable(
                name: "platform_control_states",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    maintenance_message_arabic = table.Column<string>(type: "text", nullable: true),
                    maintenance_message_english = table.Column<string>(type: "text", nullable: true),
                    card_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    wallet_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_control_states", x => x.id);
                    table.CheckConstraint("ck_platform_control_states_state", "state IN (1, 2)");
                    table.CheckConstraint("ck_platform_control_states_version", "version > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_platform_control_states_version",
                schema: "finance",
                table: "platform_control_states",
                column: "version",
                unique: true);

            // Deterministic initial Operational state. Existing Card/Wallet
            // behavior is preserved (both enabled); no migration may run
            // without exactly this row, so a missing record means the control
            // plane is unreadable and gated operations fail closed.
            migrationBuilder.InsertData(
                schema: "finance",
                table: "platform_control_states",
                columns: new[]
                {
                    "id",
                    "state",
                    "maintenance_message_arabic",
                    "maintenance_message_english",
                    "card_enabled",
                    "wallet_enabled",
                    "version",
                    "updated_on_utc"
                },
                values: new object[]
                {
                    new Guid("0198e2c2-4444-7777-8888-0000000000e4"),
                    1,
                    null,
                    null,
                    true,
                    true,
                    1,
                    new DateTime(2026, 10, 19, 6, 16, 22, 0, DateTimeKind.Utc)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "finance",
                table: "platform_control_states",
                keyColumn: "id",
                keyValue: new Guid("0198e2c2-4444-7777-8888-0000000000e4"));

            migrationBuilder.DropTable(
                name: "platform_control_audit_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "platform_control_states",
                schema: "finance");
        }
    }
}
