using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcsScheduler.Migrations
{
    /// <inheritdoc />
    public partial class AddIcalToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IcalToken",
                table: "NetControllers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NetControllers_IcalToken",
                table: "NetControllers",
                column: "IcalToken",
                unique: true,
                filter: "[IcalToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NetControllers_IcalToken",
                table: "NetControllers");

            migrationBuilder.DropColumn(
                name: "IcalToken",
                table: "NetControllers");
        }
    }
}
