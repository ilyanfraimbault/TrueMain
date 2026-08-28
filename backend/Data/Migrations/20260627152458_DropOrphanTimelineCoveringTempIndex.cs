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
        // any leftover temp index. IF EXISTS so it is a no-op on databases that never
        // saw the failed build (dev, fresh installs) — which is every database that
        // still has to run this migration, since the ones that did see it already
        // carry the migration in __EFMigrationsHistory.
        //
        // The DROP was CONCURRENTLY + suppressTransaction; that flag only had an effect
        // for Database.MigrateAsync(), while the deploy path pipes an `--idempotent`
        // script into `psql --single-transaction`, where each statement lives in a
        // `DO $EF$ ... END $EF$` block and Postgres rejects CONCURRENTLY (#1227).
        // Dropping an index takes a brief ACCESS EXCLUSIVE lock either way, and here it
        // resolves to a no-op.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_mp_timeline_snapshots_lookup_covering\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the orphan index was invalid and served nothing; there is
            // nothing meaningful to recreate.
        }
    }
}
