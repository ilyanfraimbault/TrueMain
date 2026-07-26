using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXminTokensAndDropRedundantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_PlatformId_Puuid",
                table: "main_champion_stats");

            migrationBuilder.DropIndex(
                name: "IX_main_candidates_ChampionId",
                table: "main_candidates");

            migrationBuilder.DropIndex(
                name: "IX_main_candidates_PlatformId",
                table: "main_candidates");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "riot_accounts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "main_candidates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "riot_accounts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "main_candidates");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_PlatformId_Puuid",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "Puuid" });

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_ChampionId",
                table: "main_candidates",
                column: "ChampionId");

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_PlatformId",
                table: "main_candidates",
                column: "PlatformId");
        }
    }
}
