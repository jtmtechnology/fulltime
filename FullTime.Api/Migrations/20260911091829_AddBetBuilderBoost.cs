using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBetBuilderBoost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LastBetBuilderBoostDate",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BetBuilderBoosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetBuilderBoosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BetBuilderBoosts_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BetBuilderBoosts_Date",
                table: "BetBuilderBoosts",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BetBuilderBoosts_MatchId",
                table: "BetBuilderBoosts",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BetBuilderBoosts");

            migrationBuilder.DropColumn(
                name: "LastBetBuilderBoostDate",
                table: "Users");
        }
    }
}
