using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class ResetItemContextForRecalibratedThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two draft-axis thresholds were recalibrated against the measured champion
            // profiles: crowd control sat above the 90th percentile (so no game ever landed
            // in the High bucket) and sustain sat below the median (so nearly every game
            // did). The counters are additive and bucketed at fold time, so the rows already
            // written carry the old bucketing and cannot be corrected in place — a later
            // run would simply add correctly-bucketed games to incorrectly-bucketed ones and
            // the axis would mean two different things at once.
            //
            // So the counters and the verdicts derived from them are dropped and every match
            // is re-armed, which is the rule `decisions/data-aggregation.md` already states
            // for tightening a fold's gate. This is cheap and safe precisely because the
            // fold is young: preprod holds one day of it and production has never run it at
            // all, so on production both statements touch zero rows.
            //
            // DELETE rather than TRUNCATE: it is transactional alongside the UPDATE below,
            // and at this size the difference costs nothing.
            migrationBuilder.Sql("""
                DELETE FROM champion_item_context_verdicts;
                DELETE FROM champion_item_context_stats;
                DELETE FROM champion_item_context_totals;
                """);

            // Only the matches that were actually folded, so the write set is the fold's own
            // backlog rather than every row of the largest table in the database.
            migrationBuilder.Sql("""
                UPDATE matches SET "ItemContextAggregated" = false WHERE "ItemContextAggregated";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the counters rebuild themselves from the retained matches on
            // the next few pipeline passes, and reverting the thresholds would not restore
            // the rows this dropped.
        }
    }
}
