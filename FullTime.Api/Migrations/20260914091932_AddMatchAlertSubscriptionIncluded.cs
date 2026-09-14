using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchAlertSubscriptionIncluded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true, not false: every row that already exists predates this column and was
            // created under the old semantics where a row's mere existence meant "included" (see
            // MatchAlertSubscription.Included's own comment) - defaulting new rows to false here
            // would silently flip every existing subscriber's match off.
            migrationBuilder.AddColumn<bool>(
                name: "Included",
                table: "MatchAlertSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Included",
                table: "MatchAlertSubscriptions");
        }
    }
}
