using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeSupportContactNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "support_phone",
                schema: "cms",
                table: "support_contacts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "support_email",
                schema: "cms",
                table: "support_contacts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "support_phone",
                schema: "cms",
                table: "support_contacts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "support_email",
                schema: "cms",
                table: "support_contacts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldNullable: true);
        }
    }
}
