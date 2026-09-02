using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Data-only migration: drops the synergy, powerspike and matchup rows of the
    /// <b>live</b> patches and re-arms their per-match flags, so all four folds rebuild
    /// them under the shared <c>Data.Aggregation.ChampionCohort</c> — mains of the
    /// champion, remakes excluded — instead of the wider "any account we know, any game
    /// length" they were accumulated with (#1365).
    ///
    /// <para>
    /// <b>Why the matchup table is in here too.</b> #1087 already gave it the mains
    /// cohort, so its population is right; what is not is the remake clause, which is new
    /// in #1365. Both matchup folds are additive and flagged, so every live-patch row
    /// folded since #1087 keeps the remakes baked into its <c>Games</c> / <c>Wins</c> /
    /// <c>LaneGames</c> counters for as long as the patch is served, diluted rather than
    /// corrected by remake-free rows arriving after the deploy. Re-folding is cheap here
    /// and loses nothing: the lane fold reads the 15-minute mark, which retention keeps
    /// as one of the canonical marks (#772).
    /// </para>
    ///
    /// <para>
    /// <b>Why a delete and not a filter going forward.</b> Both folds are additive
    /// (<c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>) and gated by a per-match
    /// flag, so tightening the cohort in code corrects nothing already written: the patch
    /// in flight would end up half-folded under each cohort — worse than either, and
    /// undiagnosable from the numbers. This is the same treatment #1087 gave
    /// <c>champion_matchup_stats</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Why not a TRUNCATE, this time.</b> #1087 could wipe its whole table because the
    /// panel moved to a per-patch scope in the same change, so the frozen patches it
    /// destroyed were no longer readable. These aggregates are read across patches
    /// and hold history whose source matches retention has already deleted (#466) — a
    /// TRUNCATE would delete numbers that can never be recomputed. So the delete is
    /// scoped to the patches that can be re-folded, and every older patch keeps the rows
    /// it was folded with, under the old cohort. That is a deliberate seam: a frozen
    /// patch's synergy and powerspike numbers count any tracked account and its matchups
    /// count remakes, the live ones do neither, and <c>decisions.md</c> says so.
    /// </para>
    ///
    /// <para>
    /// <b>What "live" means here.</b> The patches that still have matches — exactly the
    /// set <c>ChampionPatternSourceRowReader.LoadLivePatchKeysAsync</c> uses to decide
    /// which aggregate scopes may be rebuilt, and by construction the window
    /// <c>MatchDataRetentionProcess</c> keeps (it deletes whole patches beyond
    /// <c>RetainedPatchCount</c>, so what is left in <c>matches</c> is the retained set).
    /// <c>matches."Patch"</c> is the stored generated column carrying the normalised
    /// major.minor (#1368), NULL when the version does not parse — the raw
    /// <c>GameVersion</c> is the fallback because that is what the folds write in that
    /// case. No queue clause: only queue 420 is stored (#680).
    /// </para>
    ///
    /// <para>
    /// <b>What the re-fold does not recover.</b> Power spikes need the dense per-minute
    /// timeline grid, which retention prunes to the canonical marks {5,10,15,20,30} the
    /// moment a match is folded (#772). A live match that has already been pruned is
    /// re-folded from those five marks only: it contributes its curve points but no event
    /// spike, since the ±3-minute window has nothing to sit on. So the spikes panel goes
    /// thin on the live patches and refills as new matches arrive — the same forward-only
    /// coverage #957 accepted, and the honest alternative to keeping a denominator that
    /// counts a different population from the header above it.
    /// </para>
    ///
    /// <para>
    /// <b>σ is reset with them.</b> <c>powerspike_sigma_stats</c> carries no patch
    /// dimension, so re-folding a match would add its spread to a total that already
    /// contains it. Emptying the table makes σ the spread over the retained window rather
    /// than a lifetime average polluted by a double count; it is a per-minute scale for
    /// the lead, not a cohort, and it converges within one cycle.
    /// </para>
    ///
    /// <para>
    /// The re-fold itself is the ingestor's ordinary batched path (MatchupLeadAggregation,
    /// LaneOutcomeAggregation, SynergyAggregation, PowerspikeAggregation), draining over
    /// the cycles after deploy. The panels read thin numbers while it drains; every row
    /// they do show is already correct, since each fold is per-match and never partially
    /// applies one.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class RefoldChampionPanelsOnChampionCohort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One statement per table rather than a shared temp table: this runs as an
            // idempotent script piped into the VPS's Postgres ahead of the deploy
            // (docs/production-migrations.md), where a temp table's lifetime is one more
            // thing to reason about for five small deletes.
            const string livePatches =
                """
                SELECT DISTINCT COALESCE(m."Patch", m."GameVersion") AS patch FROM matches m
                """;

            foreach (var table in new[]
                     {
                         "champion_synergy_stats",
                         "champion_synergy_baseline_stats",
                         "champion_powerspike_curve_stats",
                         "champion_powerspike_event_stats",
                         // Both matchup folds write this one; #1087's cohort was already
                         // right, its remake clause is not.
                         "champion_matchup_stats"
                     })
            {
                migrationBuilder.Sql(
                    $"""
                     DELETE FROM {table} AS t
                     USING ({livePatches}) AS live
                     WHERE t."Patch" = live.patch;
                     """);
            }

            // No patch dimension to scope by — see the summary.
            migrationBuilder.Sql("""DELETE FROM powerspike_sigma_stats;""");

            // One pass for all four flags rather than four scans. The WHERE keeps it from
            // rewriting rows that are already pending — on a freshly restored database
            // that is every row, and the update would be a no-op rewrite of the table.
            // Every retained match is by definition on a live patch, so the flags are
            // cleared wholesale rather than re-deriving the same set a third time.
            migrationBuilder.Sql(
                """
                UPDATE matches
                SET "SynergyAggregated" = false,
                    "PowerspikeAggregated" = false,
                    "MatchupLeadAggregated" = false,
                    "LaneOutcomeAggregated" = false
                WHERE "SynergyAggregated" OR "PowerspikeAggregated"
                   OR "MatchupLeadAggregated" OR "LaneOutcomeAggregated";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Down would have to restore counts folded from matches
            // whose dense timelines are gone, which no amount of SQL can do. Re-running
            // Up is idempotent and is the recovery path.
        }
    }
}
