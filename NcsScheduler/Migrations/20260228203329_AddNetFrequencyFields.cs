using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcsScheduler.Migrations
{
    /// <inheritdoc />
    public partial class AddNetFrequencyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FrequencyMhz",
                table: "Nets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrequencyRange",
                table: "Nets",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FrequencyMhz",
                table: "Nets");

            migrationBuilder.DropColumn(
                name: "FrequencyRange",
                table: "Nets");
        }
    }
}
