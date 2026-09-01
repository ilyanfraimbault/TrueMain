using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTruemainPopulationToChampionAggregateScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes");

            migrationBuilder.AddColumn<bool>(
                name: "IsMain",
                table: "champion_aggregate_scopes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes",
                columns: new[] { "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "elo_bracket", "IsMain" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes");

            migrationBuilder.DropColumn(
                name: "IsMain",
                table: "champion_aggregate_scopes");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes",
                columns: new[] { "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "elo_bracket" });
        }
    }
}
