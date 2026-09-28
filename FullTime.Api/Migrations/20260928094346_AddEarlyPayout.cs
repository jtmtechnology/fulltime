using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEarlyPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AwayTwoGoalLeadSince",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HomeTwoGoalLeadSince",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidOutEarlyAt",
                table: "BetLegPicks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayTwoGoalLeadSince",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomeTwoGoalLeadSince",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PaidOutEarlyAt",
                table: "BetLegPicks");
        }
    }
}
