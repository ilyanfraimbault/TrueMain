using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Adds the experience half of the 15-minute gap (#1111) and re-arms both matchup
    /// folds so the retained window is rebuilt carrying it.
    ///
    /// <para>
    /// <b>Why it re-folds rather than filling in from here.</b> The columns are
    /// additive counters written per match by a flag-gated fold, so adding them alone
    /// would leave every already-folded match with gold and no XP — permanently, since
    /// a patch whose matches retention has dropped can never be recomputed (#466). The
    /// read side is honest about that (<c>LaneXpDiffGames</c> is its own denominator,
    /// so an unmeasured gap reads as unknown rather than as a dead-even lane), but
    /// "honestly empty" is not the goal when a re-fold is available.
    /// </para>
    ///
    /// <para>
    /// <b>It is nearly free right now, and will not be later.</b>
    /// <c>RefoldMatchupStatsOnMainsCohort</c> (#1087) already truncates this table and
    /// has not yet run on production. Shipping this before that release means the two
    /// migrations apply back to back and the ingestor re-folds the window <em>once</em>,
    /// with gold and XP together. Preprod, which already consumed #1087, pays a second
    /// re-fold of its single retained patch — minutes of ingestion.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class AddMatchupLaneXpDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LaneXpDiffGames",
                table: "champion_matchup_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LaneXpDiffSum",
                table: "champion_matchup_stats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Same wipe-and-re-arm as #1087's migration, for the same reason: an
            // additive fold cannot backfill itself. TRUNCATE over DELETE — nothing
            // references these rows and this runs out-of-band before the deploy.
            migrationBuilder.Sql("TRUNCATE TABLE champion_matchup_stats;");
            migrationBuilder.Sql(
                """
                UPDATE matches
                SET "MatchupLeadAggregated" = false,
                    "LaneOutcomeAggregated" = false
                WHERE "MatchupLeadAggregated" OR "LaneOutcomeAggregated";
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Drops the columns only. The re-fold above is not undoable — the counters it
        /// rebuilt came from matches retention may since have deleted — so Down leaves
        /// the table as the re-fold produced it, minus the two XP columns.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaneXpDiffGames",
                table: "champion_matchup_stats");

            migrationBuilder.DropColumn(
                name: "LaneXpDiffSum",
                table: "champion_matchup_stats");
        }
    }
}
