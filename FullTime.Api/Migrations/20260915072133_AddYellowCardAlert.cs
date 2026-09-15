using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddYellowCardAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "YellowCard",
                table: "UserAlertPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YellowCard",
                table: "UserAlertPreferences");
        }
    }
}
