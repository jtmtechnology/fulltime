using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class ApiFootballOddsApiCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OddsLastFetchedAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlayerStatsResolvedAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCorners",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerName",
                table: "BetLegPicks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "BetLegPicks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlayerName",
                table: "BetBuilderMarkets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "BetBuilderMarkets",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MatchPlayerStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerName = table.Column<string>(type: "text", nullable: false),
                    Team = table.Column<int>(type: "integer", nullable: false),
                    Goals = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    ShotsOnTarget = table.Column<int>(type: "integer", nullable: false),
                    YellowCards = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchPlayerStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchPlayerStats_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchPlayerStats_MatchId",
                table: "MatchPlayerStats",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchPlayerStats");

            migrationBuilder.DropColumn(
                name: "OddsLastFetchedAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlayerStatsResolvedAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "TotalCorners",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlayerName",
                table: "BetLegPicks");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "BetLegPicks");

            migrationBuilder.DropColumn(
                name: "PlayerName",
                table: "BetBuilderMarkets");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "BetBuilderMarkets");
        }
    }
}
