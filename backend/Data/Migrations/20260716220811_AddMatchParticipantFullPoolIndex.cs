using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantFullPoolIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain, transactional CREATE INDEX — no CONCURRENTLY, no IF NOT
            // EXISTS, no out-of-band pre-creation. The full-pool composition
            // search (#563) needs this index over the whole match_participants
            // table (tracked + harvested), unlike the partial "_tracked" index.
            //
            // Measured on prod (2026-07): the table is ~2M rows / ~11 GB (far
            // smaller than the multi-tens-of-GB it once was, since #680 drained
            // non-ranked rows). A parallel btree build over three narrow columns
            // reads the heap once (~30 s of I/O measured via a seq scan) and
            // sorts ~2M keys in maintenance_work_mem — comfortably under the
            // migrator's Command Timeout=300, so it will not crash-loop startup
            // the way a slow CONCURRENTLY build did in #598. Running it inside
            // the migration transaction keeps it atomic (no INVALID index left
            // behind on failure) and, crucially, fully reproducible from the
            // migrations alone: a fresh restore rebuilds the exact schema with
            // no manual step.
            //
            // Trade-off: the build takes a SHARE lock that blocks writes to
            // match_participants for its duration. Only the API applies
            // migrations on startup, so the ingestor keeps writing during a
            // deploy and stalls for the ~tens of seconds the build runs — a
            // one-time hit the ingestor's buffering/retries absorb.
            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition", "elo_bracket" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants");
        }
    }
}
