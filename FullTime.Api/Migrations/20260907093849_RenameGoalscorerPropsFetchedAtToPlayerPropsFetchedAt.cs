using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameGoalscorerPropsFetchedAtToPlayerPropsFetchedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoalscorerPropsFetchedAt",
                table: "Matches",
                newName: "PlayerPropsFetchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayerPropsFetchedAt",
                table: "Matches",
                newName: "GoalscorerPropsFetchedAt");
        }
    }
}
