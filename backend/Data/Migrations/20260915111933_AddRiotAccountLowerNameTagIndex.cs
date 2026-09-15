using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Functional index serving <c>TruemainAccountResolver</c>'s case-insensitive Riot ID lookup,
    /// <c>lower("GameName") = @p AND lower("TagLine") = @p</c> (#1570). Every name-tag route resolves
    /// through it, and without the index each call scanned and sorted riot_accounts.
    /// </summary>
    /// <remarks>
    /// Plain DDL, not <c>CONCURRENTLY</c>: the deploy pipes an idempotent script into
    /// <c>psql --single-transaction</c>, where Postgres rejects it (#1227). A btree over two short
    /// text expressions on a few hundred thousand rows builds in seconds, holding the write lock
    /// that long once. The expression cannot be declared in the EF model, so the model and the
    /// compiled model are unchanged and the index lives in this migration only.
    /// </remarks>
    public partial class AddRiotAccountLowerNameTagIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_riot_accounts_game_name_tag_line_lower\" " +
                "ON riot_accounts (lower(\"GameName\"), lower(\"TagLine\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_riot_accounts_game_name_tag_line_lower\";");
        }
    }
}
