using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBetBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirstGoalScorerSide",
                table: "Matches",
                type: "integer",
                nullable: true);

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

            migrationBuilder.CreateTable(
                name: "BetBuilderMarkets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketType = table.Column<int>(type: "integer", nullable: false),
                    Line = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Side = table.Column<int>(type: "integer", nullable: true),
                    PredictedHomeScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedAwayScore = table.Column<int>(type: "integer", nullable: true),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetBuilderMarkets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetBuilderMarkets_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BetLegs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BetId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OddsAtPlacement = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetLegs_Bets_BetId",
                        column: x => x.BetId,
                        principalTable: "Bets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BetLegs_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BetLegPicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BetLegId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketType = table.Column<int>(type: "integer", nullable: false),
                    Line = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Side = table.Column<int>(type: "integer", nullable: true),
                    PredictedHomeScore = table.Column<int>(type: "integer", nullable: true),
                    PredictedAwayScore = table.Column<int>(type: "integer", nullable: true),
                    OddsAtPlacement = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetLegPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetLegPicks_BetLegs_BetLegId",
                        column: x => x.BetLegId,
                        principalTable: "BetLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_HighlightlyMatchId",
                table: "Matches",
                column: "HighlightlyMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BetBuilderMarkets_MatchId",
                table: "BetBuilderMarkets",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BetLegPicks_BetLegId",
                table: "BetLegPicks",
                column: "BetLegId");

            migrationBuilder.CreateIndex(
                name: "IX_BetLegs_BetId",
                table: "BetLegs",
                column: "BetId");

            migrationBuilder.CreateIndex(
                name: "IX_BetLegs_MatchId",
                table: "BetLegs",
                column: "MatchId");

            // Carry every existing BetSelection into the new BetLeg/BetLegPick shape before dropping
            // the old table, so no bet history is lost. Each old selection becomes a BetLeg with
            // exactly one BetLegPick (MarketType 0 = MatchResult, no Line). The BetLeg reuses the old
            // BetSelection's own Id — it's about to be dropped, so there's no collision risk, and it
            // avoids needing a server-generated id to link the two inserts. MatchOutcome and
            // SelectionSide share the same underlying Home=0/Draw=1/Away=2 values, so the old
            // "Pick" column maps onto the new "Side" column with no translation needed.
            migrationBuilder.Sql(
                """
                INSERT INTO "BetLegs" ("Id", "BetId", "MatchId", "OddsAtPlacement", "Outcome")
                SELECT "Id", "BetId", "MatchId", "OddsAtPlacement", "Outcome" FROM "BetSelections";

                INSERT INTO "BetLegPicks" ("Id", "BetLegId", "MarketType", "Line", "Side", "OddsAtPlacement", "Outcome")
                SELECT gen_random_uuid(), "Id", 0, NULL, "Pick", "OddsAtPlacement", "Outcome" FROM "BetSelections";
                """);

            migrationBuilder.DropTable(
                name: "BetSelections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BetSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BetId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OddsAtPlacement = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Pick = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetSelections_Bets_BetId",
                        column: x => x.BetId,
                        principalTable: "Bets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BetSelections_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BetSelections_BetId",
                table: "BetSelections",
                column: "BetId");

            migrationBuilder.CreateIndex(
                name: "IX_BetSelections_MatchId",
                table: "BetSelections",
                column: "MatchId");

            // Copy back only the single-pick, MatchResult legs — the exact shape BetSelections could
            // ever hold. Any genuine bet-builder multi-pick leg has no equivalent in the old shape and
            // is necessarily dropped on rollback; this Down() is a dev/emergency escape hatch, not a
            // guarantee of preserving bet-builder-era history (that's what the pre-bet-builder git tag
            // and DB backup are for).
            migrationBuilder.Sql(
                """
                INSERT INTO "BetSelections" ("Id", "BetId", "MatchId", "OddsAtPlacement", "Outcome", "Pick")
                SELECT l."Id", l."BetId", l."MatchId", l."OddsAtPlacement", l."Outcome", p."Side"
                FROM "BetLegs" l
                JOIN "BetLegPicks" p ON p."BetLegId" = l."Id"
                WHERE p."MarketType" = 0
                  AND (SELECT COUNT(*) FROM "BetLegPicks" p2 WHERE p2."BetLegId" = l."Id") = 1;
                """);

            migrationBuilder.DropTable(
                name: "BetBuilderMarkets");

            migrationBuilder.DropTable(
                name: "BetLegPicks");

            migrationBuilder.DropTable(
                name: "BetLegs");

            migrationBuilder.DropIndex(
                name: "IX_Matches_HighlightlyMatchId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "FirstGoalScorerSide",
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
        }
    }
}
