using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBoostAppliedToBet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoostApplied",
                table: "Bets",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoostApplied",
                table: "Bets");
        }
    }
}
