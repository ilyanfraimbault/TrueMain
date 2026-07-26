using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionMatchupLeadAggregatedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MatchupLeadAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every currently-live match already contributed to champion_matchup_stats /
            // champion_timeline_lead_stats via the old full-recompute pass (#606/#708), so
            // backfilling the flag to true here is required, not optional: without it, the
            // first incremental run (#811) would re-add every existing match's Games/Wins
            // on top of the totals the full recompute already wrote, double-counting them.
            migrationBuilder.Sql("UPDATE matches SET \"MatchupLeadAggregated\" = true;");

            migrationBuilder.CreateIndex(
                name: "IX_matches_matchup_lead_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"MatchupLeadAggregated\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_matchup_lead_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "MatchupLeadAggregated",
                table: "matches");
        }
    }
}
