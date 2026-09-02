using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Data-only migration: empties <c>champion_matchup_stats</c> and re-arms both folds
    /// that write it, so the table is rebuilt under the mains cohort
    /// (<c>Data.Aggregation.MatchupCohort</c>, renamed <c>ChampionCohort</c> in #1365) instead of the wider "any account we know"
    /// one it was accumulated with.
    ///
    /// <para>
    /// <b>Why a wipe and not a filter going forward.</b> Both folds are additive
    /// (<c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>) and gated by a per-match
    /// flag, so tightening the cohort in code corrects nothing already written: the patch
    /// in flight would end up half-folded under each cohort — worse than either, and
    /// undiagnosable from the numbers. Rebuilding is the only way to a consistent table.
    /// </para>
    ///
    /// <para>
    /// <b>What it costs.</b> Matches are retained for
    /// <c>MatchDataRetention:RetainedPatchCount</c> patches (2 on prod), so only those
    /// patches can be re-folded; matchup rows for patches whose raw matches are already
    /// gone are deleted and cannot come back (#466). That was accepted deliberately when
    /// this shipped — the panel moved to a per-patch scope in the same change, so the
    /// patches being dropped were no longer readable anyway.
    /// </para>
    ///
    /// <para>
    /// The re-fold itself is the ingestor's ordinary batched path
    /// (<c>MatchupLeadAggregation</c> then <c>LaneOutcomeAggregation</c>, 500 matches per
    /// batch), draining over the cycles after deploy. The panel reads thin numbers while
    /// it drains — every row it does show is already correct, since the fold is per-match
    /// and never partially applies one.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class RefoldMatchupStatsOnMainsCohort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TRUNCATE, not DELETE: nothing references these rows, and this runs
            // out-of-band ahead of the deploy (docs/production-migrations.md), where a
            // full-table DELETE would be pure WAL for no benefit.
            migrationBuilder.Sql("TRUNCATE TABLE champion_matchup_stats;");

            // One pass for both flags rather than two scans. The WHERE keeps it from
            // rewriting rows that are already pending — on a freshly restored database
            // that is every row, and the update would be a no-op rewrite of the table.
            migrationBuilder.Sql(
                """
                UPDATE matches
                SET "MatchupLeadAggregated" = false,
                    "LaneOutcomeAggregated" = false
                WHERE "MatchupLeadAggregated" OR "LaneOutcomeAggregated";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Down would have to restore counts folded from matches
            // that retention has since deleted, which no amount of SQL can do. Re-running
            // Up is idempotent and is the recovery path.
        }
    }
}
