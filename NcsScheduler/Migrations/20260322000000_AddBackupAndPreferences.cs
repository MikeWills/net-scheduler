using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NcsScheduler.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BackupRequested",
                table: "NetSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "NetPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NetControllerId = table.Column<int>(type: "INTEGER", nullable: false),
                    NetId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NetPreferences_NetControllers_NetControllerId",
                        column: x => x.NetControllerId,
                        principalTable: "NetControllers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NetPreferences_Nets_NetId",
                        column: x => x.NetId,
                        principalTable: "Nets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NetPreferences_NetControllerId_NetId",
                table: "NetPreferences",
                columns: new[] { "NetControllerId", "NetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NetPreferences_NetId",
                table: "NetPreferences",
                column: "NetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NetPreferences");

            migrationBuilder.DropColumn(
                name: "BackupRequested",
                table: "NetSessions");
        }
    }
}
