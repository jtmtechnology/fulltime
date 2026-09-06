using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Sandbox.Migrations
{
    /// <inheritdoc />
    public partial class CorrectScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Side",
                table: "PlayerPropMarkets",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "PredictedAwayScore",
                table: "PlayerPropMarkets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PredictedHomeScore",
                table: "PlayerPropMarkets",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PredictedAwayScore",
                table: "PlayerPropMarkets");

            migrationBuilder.DropColumn(
                name: "PredictedHomeScore",
                table: "PlayerPropMarkets");

            migrationBuilder.AlterColumn<string>(
                name: "Side",
                table: "PlayerPropMarkets",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
