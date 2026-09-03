using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Retires the lane fold's own pending flag, and rebuilds the live patches under the
    /// single fold that replaces it (#1445).
    ///
    /// <para>
    /// <b>What was wrong.</b> <c>Games</c>/<c>Wins</c> and the lane counters on one
    /// <c>champion_matchup_stats</c> row were written by two processes off two flags.
    /// The row key includes <c>match_participants.elo_bracket</c>, which is stamped
    /// asynchronously once an account's first rank snapshot lands, and since #1362 the
    /// fetch lane ingests matches while the aggregate lane is mid-run. A match folded on
    /// the lane side before the stamping and on the game side after it put its two halves
    /// on two different rows: preprod held 10 rows with more <c>LaneGames</c> than
    /// <c>Games</c> — the admin's data-quality check calls that arithmetically
    /// impossible, and it is — beside real-band rows whose lane sample was quietly short.
    /// </para>
    ///
    /// <para>
    /// <b>Why a re-fold and not a repair.</b> The folds are additive and leave no
    /// per-match trace, so the misplaced counters cannot be moved back: nothing in the
    /// table says which games a row's <c>LaneGames</c> came from. Deleting the live
    /// patches and re-folding them under one pass is exact, and cheap — the 15-minute
    /// mark the lane verdict reads is one of the canonical marks retention keeps (#772),
    /// so nothing is lost by reading it again.
    /// </para>
    ///
    /// <para>
    /// <b>Scoped to the live patches</b>, exactly as #1365 was. Rows for patches whose
    /// matches retention has already dropped can never be recomputed (#466); they keep
    /// the numbers they were folded with, split rows included. That is a seam, not an
    /// oversight: a frozen patch is served as it was measured, and only the window that
    /// still has matches behind it can be restated. <c>matches</c> only ever holds live
    /// patches — retention drops whole patches — so re-arming the flag wholesale re-folds
    /// exactly the set the delete emptied.
    /// </para>
    ///
    /// <para>
    /// <b>Then the flag goes.</b> <c>LaneOutcomeAggregated</c> and its partial index are
    /// dropped last, after the re-arm has been expressed in terms of the surviving flag,
    /// so a match cannot end up pending under a column that no longer exists. The
    /// re-fold itself is the ingestor's ordinary batched path, draining over the cycles
    /// after deploy; the panel reads thin numbers while it does, and every row it shows
    /// is already whole, since the fold is per-match and never partially applies one.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class MergeLaneOutcomeFoldIntoMatchupFold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM champion_matchup_stats AS t
                USING (SELECT DISTINCT COALESCE(m."Patch", m."GameVersion") AS patch FROM matches m) AS live
                WHERE t."Patch" = live.patch;
                """);

            // Every retained match is on a live patch by construction, so the whole
            // column is re-armed rather than re-deriving the same set a second time. The
            // WHERE keeps this from rewriting rows that are already pending — on a
            // freshly restored database that is every row.
            migrationBuilder.Sql(
                """
                UPDATE matches
                SET "MatchupLeadAggregated" = false
                WHERE "MatchupLeadAggregated";
                """);

            migrationBuilder.DropIndex(
                name: "IX_matches_lane_outcome_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "LaneOutcomeAggregated",
                table: "matches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The column and its index come back empty-handed: which matches had been
            // folded for their lanes is exactly what the drop above forgets, and the
            // counters cannot be un-summed. Re-running Up is the recovery path — it is
            // idempotent, since it re-folds from the matches themselves.
            migrationBuilder.AddColumn<bool>(
                name: "LaneOutcomeAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_matches_lane_outcome_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"LaneOutcomeAggregated\" = false");
        }
    }
}
