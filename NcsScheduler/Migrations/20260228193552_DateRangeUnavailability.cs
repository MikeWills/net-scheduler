using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcsScheduler.Migrations
{
    /// <inheritdoc />
    public partial class DateRangeUnavailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UnavailableDate",
                table: "Unavailabilities",
                newName: "StartDate");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "Unavailabilities",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "Unavailabilities");

            migrationBuilder.RenameColumn(
                name: "StartDate",
                table: "Unavailabilities",
                newName: "UnavailableDate");
        }
    }
}
