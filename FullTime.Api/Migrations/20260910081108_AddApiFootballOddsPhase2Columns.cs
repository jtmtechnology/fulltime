using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiFootballOddsPhase2Columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FoulsCommitted",
                table: "MatchPlayerStats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApiFootballOddsLastFetchedAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwayCards",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwayCorners",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeCards",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeCorners",
                table: "Matches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FoulsCommitted",
                table: "MatchPlayerStats");

            migrationBuilder.DropColumn(
                name: "ApiFootballOddsLastFetchedAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "AwayCards",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "AwayCorners",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeCards",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeCorners",
                table: "Matches");
        }
    }
}
