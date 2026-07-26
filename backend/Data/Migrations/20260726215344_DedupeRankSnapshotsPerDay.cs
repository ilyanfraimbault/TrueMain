using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DedupeRankSnapshotsPerDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical cleanup: keep only the most-recently-captured row per
            // account per UTC calendar day, matching the writer's new one-row-per-day
            // behaviour (RankSnapshotWriter.Ingest) so old data isn't left denser
            // than anything the app can produce going forward.
            // CapturedAtUtc is timestamptz, so date_trunc() on it directly is only
            // STABLE (it'd depend on the session's TimeZone setting) — not usable in
            // an index expression. "AT TIME ZONE 'UTC'" first collapses it to a plain
            // UTC timestamp, which date_trunc() truncates immutably.
            migrationBuilder.Sql(
                """
                DELETE FROM rank_snapshots rs
                USING (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "RiotAccountId", date_trunc('day', "CapturedAtUtc" AT TIME ZONE 'UTC')
                               ORDER BY "CapturedAtUtc" DESC
                           ) AS rn
                    FROM rank_snapshots
                ) ranked
                WHERE rs."Id" = ranked."Id" AND ranked.rn > 1;
                """);

            // Enforces the one-row-per-account-per-day invariant at the database
            // level, as a safety net beyond the application-level dedup in
            // RankSnapshotWriter.Ingest.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_rank_snapshots_account_day"
                ON rank_snapshots ("RiotAccountId", (date_trunc('day', "CapturedAtUtc" AT TIME ZONE 'UTC')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_rank_snapshots_account_day";""");
        }
    }
}
