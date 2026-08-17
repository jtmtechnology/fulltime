using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Balance",
                table: "LeagueMemberships",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 1000m);

            migrationBuilder.AddColumn<Guid>(
                name: "LeagueId",
                table: "Bets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bets_LeagueId",
                table: "Bets",
                column: "LeagueId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bets_Leagues_LeagueId",
                table: "Bets",
                column: "LeagueId",
                principalTable: "Leagues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bets_Leagues_LeagueId",
                table: "Bets");

            migrationBuilder.DropIndex(
                name: "IX_Bets_LeagueId",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "Balance",
                table: "LeagueMemberships");

            migrationBuilder.DropColumn(
                name: "LeagueId",
                table: "Bets");
        }
    }
}
