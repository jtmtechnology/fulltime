using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Sandbox.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerPropSide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Side",
                table: "PlayerPropMarkets",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Side",
                table: "PlayerPropMarkets");
        }
    }
}
