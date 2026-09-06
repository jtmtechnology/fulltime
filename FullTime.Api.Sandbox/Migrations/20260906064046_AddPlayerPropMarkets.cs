using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Sandbox.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerPropMarkets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "PlayerPropMarkets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketKey = table.Column<string>(type: "text", nullable: false),
                    PlayerName = table.Column<string>(type: "text", nullable: false),
                    Bookmaker = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Point = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerPropMarkets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerPropMarkets_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerPropMarkets_MatchId",
                table: "PlayerPropMarkets",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerPropMarkets");

            migrationBuilder.DropColumn(
                name: "AwayTeamLogoUrl",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeTeamLogoUrl",
                table: "Matches");
        }
    }
}
