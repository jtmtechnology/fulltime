using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetireHighlightlyShadowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_HighlightlyMatchId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HighlightlyAwayTeamId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HighlightlyHomeTeamId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HighlightlyLeagueId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HighlightlyMatchId",
                table: "Matches");

            migrationBuilder.AddColumn<string>(
                name: "AwayTeamLogoUrl",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeTeamLogoUrl",
                table: "Matches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayTeamLogoUrl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeTeamLogoUrl",
                table: "Matches");

            migrationBuilder.AddColumn<long>(
                name: "HighlightlyAwayTeamId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HighlightlyHomeTeamId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HighlightlyLeagueId",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HighlightlyMatchId",
                table: "Matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HighlightlyMatchId",
                table: "Matches",
                column: "HighlightlyMatchId");
        }
    }
}
