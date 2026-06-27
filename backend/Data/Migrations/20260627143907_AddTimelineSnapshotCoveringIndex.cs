using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineSnapshotCoveringIndex : Migration
    {
        // EF's conventional name for the unique (MatchId, ParticipantId, IntervalMinute)
        // index, truncated to Postgres' 63-char identifier limit.
        private const string IndexName =
            "IX_match_participant_timeline_snapshots_MatchId_ParticipantId_~";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rebuild the unique snapshot index as a covering index (INCLUDE the
            // measured columns) so the champion timeline-leads joins are served as
            // index-only scans instead of one random heap fetch per matched row —
            // the heap traffic that made the read time out once parallel query was
            // disabled (#594).
            //
            // CONCURRENTLY so building it on the large, ingestion-hot snapshot table
            // takes no write lock; it cannot run inside a transaction, hence
            // suppressTransaction on every statement. Build the covering index under
            // a temp name first, then drop the old index and rename into place, so
            // the uniqueness / dedup guard is never absent.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_mp_timeline_snapshots_lookup_covering\" " +
                "ON match_participant_timeline_snapshots (\"MatchId\", \"ParticipantId\", \"IntervalMinute\") " +
                "INCLUDE (\"TotalGold\", \"MinionsKilled\", \"JungleMinionsKilled\", \"Kills\", \"Level\", \"Xp\", \"DamageToChampions\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"ALTER INDEX \"IX_mp_timeline_snapshots_lookup_covering\" RENAME TO \"{IndexName}\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric rebuild back to the plain (non-covering) unique index.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_mp_timeline_snapshots_lookup_plain\" " +
                "ON match_participant_timeline_snapshots (\"MatchId\", \"ParticipantId\", \"IntervalMinute\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"ALTER INDEX \"IX_mp_timeline_snapshots_lookup_plain\" RENAME TO \"{IndexName}\";",
                suppressTransaction: true);
        }
    }
}
