using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullTime.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSpinReminderToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LastSpinReminderDate",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UtcOffsetMinutes",
                table: "Users",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSpinReminderDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UtcOffsetMinutes",
                table: "Users");
        }
    }
}
