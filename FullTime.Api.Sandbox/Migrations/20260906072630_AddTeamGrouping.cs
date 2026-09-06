using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Sandbox.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "PlayerPropMarkets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AwayTeamId",
                table: "Matches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "HomeTeamId",
                table: "Matches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Team",
                table: "PlayerPropMarkets");

            migrationBuilder.DropColumn(
                name: "AwayTeamId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeTeamId",
                table: "Matches");
        }
    }
}
