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

        private const string TempCoveringName = "IX_mp_timeline_snapshots_lookup_covering";
        private const string TempPlainName = "IX_mp_timeline_snapshots_lookup_plain";

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
            //
            // Pre-drop the temp index so the build is self-healing: an interrupted
            // CREATE INDEX CONCURRENTLY (OOM kill, pod restart) leaves an INVALID
            // index under the temp name that IF NOT EXISTS would silently keep —
            // and the later RENAME would then promote that invalid index to the
            // production name, losing both the index-only scan and the uniqueness
            // guard. Dropping first guarantees we always rebuild a valid index.
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{TempCoveringName}\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"CREATE UNIQUE INDEX CONCURRENTLY \"{TempCoveringName}\" " +
                "ON match_participant_timeline_snapshots (\"MatchId\", \"ParticipantId\", \"IntervalMinute\") " +
                "INCLUDE (\"TotalGold\", \"MinionsKilled\", \"JungleMinionsKilled\", \"Kills\", \"Level\", \"Xp\", \"DamageToChampions\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"ALTER INDEX \"{TempCoveringName}\" RENAME TO \"{IndexName}\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric rebuild back to the plain (non-covering) unique index, with
            // the same self-healing pre-drop of the temp index.
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{TempPlainName}\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"CREATE UNIQUE INDEX CONCURRENTLY \"{TempPlainName}\" " +
                "ON match_participant_timeline_snapshots (\"MatchId\", \"ParticipantId\", \"IntervalMinute\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"DROP INDEX CONCURRENTLY IF EXISTS \"{IndexName}\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                $"ALTER INDEX \"{TempPlainName}\" RENAME TO \"{IndexName}\";",
                suppressTransaction: true);
        }
    }
}
