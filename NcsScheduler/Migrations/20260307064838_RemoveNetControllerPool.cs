using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcsScheduler.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNetControllerPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NetControllerPool");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NetControllerPool",
                columns: table => new
                {
                    NetId = table.Column<int>(type: "INTEGER", nullable: false),
                    NetControllerId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetControllerPool", x => new { x.NetId, x.NetControllerId });
                    table.ForeignKey(
                        name: "FK_NetControllerPool_NetControllers_NetControllerId",
                        column: x => x.NetControllerId,
                        principalTable: "NetControllers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NetControllerPool_Nets_NetId",
                        column: x => x.NetId,
                        principalTable: "Nets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NetControllerPool_NetControllerId",
                table: "NetControllerPool",
                column: "NetControllerId");
        }
    }
}
