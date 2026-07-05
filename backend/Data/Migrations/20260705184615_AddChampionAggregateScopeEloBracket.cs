using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionAggregateScopeEloBracket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_scopes_RiotAccountId_ChampionId_GameVer~1",
                table: "champion_aggregate_scopes");

            migrationBuilder.AddColumn<string>(
                name: "elo_bracket",
                table: "champion_aggregate_scopes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes",
                columns: new[] { "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "elo_bracket" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_RiotAccountId_ChampionId_GameVer~1",
                table: "champion_aggregate_scopes",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "elo_bracket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes");

            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_scopes_RiotAccountId_ChampionId_GameVer~1",
                table: "champion_aggregate_scopes");

            migrationBuilder.DropColumn(
                name: "elo_bracket",
                table: "champion_aggregate_scopes");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_RiotAccountId_ChampionId_GameVer~1",
                table: "champion_aggregate_scopes",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position" },
                unique: true);
        }
    }
}
