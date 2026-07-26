using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropRiotAccountRiotIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_GameName_TagLine_PlatformId",
                table: "riot_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_GameName_TagLine_PlatformId",
                table: "riot_accounts",
                columns: new[] { "GameName", "TagLine", "PlatformId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_GameName_TagLine_PlatformId",
                table: "riot_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_GameName_TagLine_PlatformId",
                table: "riot_accounts",
                columns: new[] { "GameName", "TagLine", "PlatformId" },
                unique: true);
        }
    }
}
