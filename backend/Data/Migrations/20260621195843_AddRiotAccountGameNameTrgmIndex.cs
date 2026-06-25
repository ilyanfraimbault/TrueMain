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
            // Both statements run outside a transaction: CREATE INDEX
            // CONCURRENTLY (so building it never takes a write lock on the
            // ingestor-hot riot_accounts table) cannot run inside one, and
            // CREATE EXTENSION is happy without one too — keeping the whole
            // migration non-transactional avoids mixing the two.
            migrationBuilder.Sql(
                "CREATE EXTENSION IF NOT EXISTS pg_trgm;",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_riot_accounts_game_name_trgm\" " +
                "ON riot_accounts USING gin (\"GameName\" gin_trgm_ops);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Leave pg_trgm installed on the way down — other objects may come
            // to rely on it, and dropping a shared extension is riskier than the
            // index it backed. Only the index is reverted.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_riot_accounts_game_name_trgm\";",
                suppressTransaction: true);
        }
    }
}
