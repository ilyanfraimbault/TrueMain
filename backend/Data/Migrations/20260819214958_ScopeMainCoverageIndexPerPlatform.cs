using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeMainCoverageIndexPerPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "ChampionId" },
                filter: "\"IsMain\" AND \"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats",
                column: "ChampionId",
                filter: "\"IsMain\" AND \"IsActive\"");
        }
    }
}
