using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Families.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionRetirementAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_plan_retirement_audits",
                schema: "families",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    plan_version = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    old_availability = table.Column<bool>(type: "boolean", nullable: false),
                    new_availability = table.Column<bool>(type: "boolean", nullable: false),
                    retired_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plan_retirement_audits", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscription_plan_retirement_audits_subscription_plan_versi~",
                        column: x => x.plan_version_id,
                        principalSchema: "families",
                        principalTable: "subscription_plan_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plan_retirement_audits_plan_version_id",
                schema: "families",
                table: "subscription_plan_retirement_audits",
                column: "plan_version_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_plan_retirement_audits",
                schema: "families");
        }
    }
}
