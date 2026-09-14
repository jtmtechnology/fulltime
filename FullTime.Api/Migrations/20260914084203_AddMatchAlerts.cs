using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FavouriteLeagues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeagueId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavouriteLeagues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavouriteLeagues_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FavouriteTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavouriteTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavouriteTeams_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchAlertSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchAlertSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchAlertSubscriptions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchAlertSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SentMatchAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertType = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentMatchAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentMatchAlerts_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SentMatchAlerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAlertPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineupsOut = table.Column<bool>(type: "boolean", nullable: false),
                    Kickoff = table.Column<bool>(type: "boolean", nullable: false),
                    HalfTime = table.Column<bool>(type: "boolean", nullable: false),
                    Goal = table.Column<bool>(type: "boolean", nullable: false),
                    RedCard = table.Column<bool>(type: "boolean", nullable: false),
                    FullTime = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAlertPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAlertPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FavouriteLeagues_UserId_LeagueId",
                table: "FavouriteLeagues",
                columns: new[] { "UserId", "LeagueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FavouriteTeams_UserId_TeamId",
                table: "FavouriteTeams",
                columns: new[] { "UserId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchAlertSubscriptions_MatchId",
                table: "MatchAlertSubscriptions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchAlertSubscriptions_UserId_MatchId",
                table: "MatchAlertSubscriptions",
                columns: new[] { "UserId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentMatchAlerts_MatchId",
                table: "SentMatchAlerts",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SentMatchAlerts_UserId_MatchId_AlertType_Sequence",
                table: "SentMatchAlerts",
                columns: new[] { "UserId", "MatchId", "AlertType", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAlertPreferences_UserId",
                table: "UserAlertPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FavouriteLeagues");

            migrationBuilder.DropTable(
                name: "FavouriteTeams");

            migrationBuilder.DropTable(
                name: "MatchAlertSubscriptions");

            migrationBuilder.DropTable(
                name: "SentMatchAlerts");

            migrationBuilder.DropTable(
                name: "UserAlertPreferences");
        }
    }
}
