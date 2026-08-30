using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRiotAccountGameNameTrgmIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Trigram index backing the truemain name search
            // (GET /truemains/search): a case-insensitive substring match
            // (`"GameName" ILIKE '%q%'`) cannot use a plain btree, so it gets a
            // GIN index over the column's trigrams. pg_trgm is a trusted
            // extension since PG13, so the migration role can create it.
            //
            // Both statements are plain and transactional. The index build used
            // CONCURRENTLY + suppressTransaction, which only ever had an effect for
            // Database.MigrateAsync() — the deploy path is a `--idempotent` script
            // piped into `psql --single-transaction`, where each statement sits in a
            // `DO $EF$ ... END $EF$` block and Postgres rejects CONCURRENTLY outright
            // (#1227). The body only re-executes on a database missing this migration
            // from __EFMigrationsHistory, i.e. a brand-new one where riot_accounts is
            // empty and the GIN build is instantaneous.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_riot_accounts_game_name_trgm\" " +
                "ON riot_accounts USING gin (\"GameName\" gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Leave pg_trgm installed on the way down — other objects may come
            // to rely on it, and dropping a shared extension is riskier than the
            // index it backed. Only the index is reverted.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_riot_accounts_game_name_trgm\";");
        }
    }
}
