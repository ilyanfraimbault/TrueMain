using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropOrphanTimelineCoveringTempIndex : Migration
    {
        // A previously shipped migration tried to build a covering version of the
        // snapshot index CONCURRENTLY under this temp name before swapping it in.
        // On large, ingestion-hot databases that build exceeded the migration
        // Command Timeout and was abandoned, leaving an INVALID index behind that
        // serves nothing. The covering-index approach has been dropped (the
        // timeline-leads timeout is fixed by the query rewrite alone), so clean up
        // any leftover temp index. CONCURRENTLY + suppressTransaction so it never
        // takes a write lock on the hot table; IF EXISTS so it is a no-op on
        // databases that never saw the failed build (dev, fresh installs).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_mp_timeline_snapshots_lookup_covering\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the orphan index was invalid and served nothing; there is
            // nothing meaningful to recreate.
        }
    }
}
