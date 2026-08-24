using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStartingBalanceToLeagueMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StartingBalance",
                table: "LeagueMemberships",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            // Backfill every existing membership to 1000 - the actual BettingOptions.StartingBalance
            // value in effect for every one of them (confirmed against production: no membership was
            // created after it dropped to 100 earlier today). Without this, Profit for every existing
            // member would be computed against the wrong baseline the moment this ships.
            migrationBuilder.Sql("UPDATE \"LeagueMemberships\" SET \"StartingBalance\" = 1000;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartingBalance",
                table: "LeagueMemberships");
        }
    }
}
