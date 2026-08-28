using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Data-only migration: re-arms the lane-outcome fold for the matches it ate while
    /// their timeline was still pending (#1223).
    ///
    /// <para>
    /// <b>The bug.</b> <c>ChampionLaneOutcomeAggregationProcess</c> selected on
    /// <c>NOT "LaneOutcomeAggregated"</c> alone, unlike its two siblings which also
    /// require <c>"TimelineIngested"</c>. A match whose timeline had not arrived yet —
    /// the ordinary case, <c>TimelineIngestionService</c> leaves the flag false on a
    /// truncated payload and re-fetches on a later run — carries no 15-minute snapshots,
    /// so it contributed nothing, and was nevertheless flagged as folded. Nothing ever
    /// looked at it again: its lane counters, gold gap and XP gap were lost for good.
    /// </para>
    ///
    /// <para>
    /// <b>What this recovers.</b> Only the matches still waiting for their timeline.
    /// They were folded for zero, so clearing the flag is exactly a no-op plus a future
    /// fold — no double counting is possible. The fold is additive
    /// (<c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>), so the set has to be
    /// provably-zero-contribution, and that is the only set that provably is:
    /// <c>TimelineIngested = false</c> means the snapshots were never written (the
    /// timeline write and the flag commit in one transaction).
    /// </para>
    ///
    /// <para>
    /// <b>What it cannot recover.</b> A match wrongly folded before its timeline landed
    /// and whose timeline has landed since is indistinguishable, row for row, from one
    /// folded correctly: same flags, same snapshots, and the fold leaves no per-match
    /// trace in <c>champion_matchup_stats</c>. Re-folding it would double-count every
    /// correctly folded match beside it, which is worse than the gap it would close.
    /// Those lanes stay missing until their matches age out of retention (#466), at which
    /// point the affected rows freeze and are replaced by the patches folded under the
    /// fixed selection.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class RearmLaneOutcomeFoldForPendingTimelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The WHERE keeps this from rewriting rows that are already pending; on a
            // healthy database the matched set is the small tail of matches whose
            // timeline re-fetch has not caught up yet.
            migrationBuilder.Sql(
                """
                UPDATE matches
                SET "LaneOutcomeAggregated" = false
                WHERE "LaneOutcomeAggregated" AND NOT "TimelineIngested";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Re-flagging these matches as folded would restore the
            // very data loss this migration undoes, and the fold that follows it is
            // idempotent per match, so there is nothing to roll back.
        }
    }
}
