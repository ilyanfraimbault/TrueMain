using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Creates the <c>pg_stat_statements</c> extension (#1366, part A).
    ///
    /// <para>
    /// The extension is what finally makes the read paths measurable: until now every
    /// investigation was hand-written <c>EXPLAIN</c> plus guesswork about which statement
    /// actually costs the most. The compose files preload the library
    /// (<c>shared_preload_libraries=pg_stat_statements</c>), which is a server start-up
    /// setting; this migration only creates the SQL-visible extension on top of it.
    /// </para>
    ///
    /// <para>
    /// <b>Why the DO block.</b> <c>CREATE EXTENSION</c> fails outright when the library was
    /// not preloaded, and that is the ordinary state of every database that does not run
    /// under the tuned compose files: a developer's local Postgres, the throwaway container
    /// the integration suite spins up, a restored dump on a plain server. Letting the raw
    /// statement run there would break the whole migration chain over a purely diagnostic
    /// extension. So the failure is caught and downgraded to a NOTICE — the schema is
    /// identical either way. It is also why the prod/preprod compose change and this
    /// migration have to ship together.
    /// </para>
    ///
    /// <para>
    /// <b>The extension will not appear on its own on preprod or prod.</b> The
    /// <c>migrate-*</c> job runs before the deploy job restarts Postgres, so the very
    /// first run of this migration meets the server that does not preload the library
    /// yet, takes the NOTICE branch, and is stamped in <c>__EFMigrationsHistory</c> all
    /// the same — nothing re-runs it. Each environment needs one manual
    /// <c>CREATE EXTENSION IF NOT EXISTS pg_stat_statements;</c> after the restart; the
    /// exact command is in <c>docs/production-migrations.md</c>.
    /// </para>
    ///
    /// <para>
    /// The block is dollar-quoted with an explicit <c>$pgss$</c> tag rather than bare
    /// <c>$$</c>: the idempotent script produced for the <c>migrate-preprod</c> /
    /// <c>migrate-prod</c> jobs nests every statement inside EF's own <c>DO $EF$</c>
    /// wrapper, and distinct tags keep that nesting unambiguous.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public partial class EnablePgStatStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $pgss$
                BEGIN
                    CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
                EXCEPTION WHEN OTHERS THEN
                    RAISE NOTICE 'pg_stat_statements could not be created (%). The server most likely does not preload the library; add shared_preload_libraries=pg_stat_statements and restart it.', SQLERRM;
                END
                $pgss$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetrically tolerant: dropping a diagnostic extension must never be the
            // reason a rollback fails.
            migrationBuilder.Sql(
                """
                DO $pgss$
                BEGIN
                    DROP EXTENSION IF EXISTS pg_stat_statements;
                EXCEPTION WHEN OTHERS THEN
                    RAISE NOTICE 'pg_stat_statements could not be dropped (%).', SQLERRM;
                END
                $pgss$;
                """);
        }
    }
}
