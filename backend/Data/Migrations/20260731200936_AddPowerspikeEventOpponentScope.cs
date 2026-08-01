using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Splits the powerspike event grain on the lane opponent the spike was measured
    /// against (#957), so the champion page's matchup filter can reach this section.
    ///
    /// <para>
    /// Existing rows keep <c>OpponentChampionId = 0</c> rather than being deleted, unlike
    /// the core-build scope migration (#890) which had to wipe the table: there, the old
    /// rows carried no build and could not be placed on the new grid at all. Here they
    /// remain a perfectly valid blend across opponents, the unscoped read sums across the
    /// column and recovers its exact previous numbers, and only the matchup filter cannot
    /// see them — which is correct, since it must not answer a matchup with a blend.
    /// Matchup coverage therefore starts empty and accumulates as new matches fold.
    /// </para>
    ///
    /// <para>
    /// The index is dropped and rebuilt rather than created concurrently: a startup
    /// migration runs inside a transaction, which rules CONCURRENTLY out. Safe here
    /// because this is a per-(champion, position, patch, elo, build, event) aggregate
    /// that retention actively prunes — orders of magnitude below the raw match tables
    /// where a startup index build is the documented hazard.
    /// </para>
    /// </summary>
    public partial class AddPowerspikeEventOpponentScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats");

            migrationBuilder.AddColumn<int>(
                name: "OpponentChampionId",
                table: "champion_powerspike_event_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "BuildFirstItemId", "BuildKeystoneId", "OpponentChampionId", "EventType", "RefId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats");

            migrationBuilder.DropColumn(
                name: "OpponentChampionId",
                table: "champion_powerspike_event_stats");

            migrationBuilder.CreateIndex(
                name: "IX_champion_powerspike_event_stats_ChampionId_TeamPosition_Pat~",
                table: "champion_powerspike_event_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "elo_bracket", "BuildFirstItemId", "BuildKeystoneId", "EventType", "RefId" },
                unique: true);
        }
    }
}
