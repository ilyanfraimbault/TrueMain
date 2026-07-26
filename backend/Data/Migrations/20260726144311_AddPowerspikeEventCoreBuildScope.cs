using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPowerspikeEventCoreBuildScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows predate the core-build dimension: they are a blend across
            // every build and cannot be attributed to one retroactively (the matches
            // they came from are flagged aggregated and their dense per-minute
            // snapshots have been pruned to the canonical marks, so they cannot be
            // re-folded either). They would land on the sentinel build 0/0, which the
            // read never asks for, so drop them rather than keep dead rows. The
            // aggregate refills from newly ingested matches.
            migrationBuilder.Sql("DELETE FROM champion_powerspike_event_stats;");

            migrationBuilder.DropIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats");

            migrationBuilder.AddColumn<int>(
                name: "BuildFirstItemId",
                table: "champion_powerspike_event_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BuildKeystoneId",
                table: "champion_powerspike_event_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "BuildFirstItemId", "BuildKeystoneId", "EventType", "RefId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats");

            migrationBuilder.DropColumn(
                name: "BuildFirstItemId",
                table: "champion_powerspike_event_stats");

            migrationBuilder.DropColumn(
                name: "BuildKeystoneId",
                table: "champion_powerspike_event_stats");

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "EventType", "RefId" },
                unique: true);
        }
    }
}
