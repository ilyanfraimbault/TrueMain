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

            // Existing (pre-bracketing) scopes carry no rank breakdown, so back
            // them into UNRANKED — a real EloBracket bucket, not "" — so the ALL
            // union still counts them and a rank filter cleanly excludes them,
            // rather than a non-canonical value lingering on frozen patches that
            // MatchDataRetention never rebuilds. Live patches get real per-tier
            // buckets on the next replace-by-scope aggregation run.
            migrationBuilder.AddColumn<string>(
                name: "elo_bracket",
                table: "champion_aggregate_scopes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UNRANKED");

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
