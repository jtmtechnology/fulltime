using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPenaltyShootoutToMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AwayPenalties",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomePenalties",
                table: "Matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WentToExtraTime",
                table: "Matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayPenalties",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "HomePenalties",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "WentToExtraTime",
                table: "Matches");
        }
    }
}
