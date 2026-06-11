using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainChampionStatCoverageIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats",
                column: "ChampionId",
                filter: "\"IsMain\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_is_main_champion",
                table: "main_champion_stats");
        }
    }
}
