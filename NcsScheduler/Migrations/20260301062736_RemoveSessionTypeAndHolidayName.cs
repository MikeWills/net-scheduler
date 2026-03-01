using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcsScheduler.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSessionTypeAndHolidayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HolidayName",
                table: "NetSessions");

            migrationBuilder.DropColumn(
                name: "SessionType",
                table: "NetSessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HolidayName",
                table: "NetSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionType",
                table: "NetSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
