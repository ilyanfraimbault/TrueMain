using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantChampionPositionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain, transactional CREATE INDEX. This originally used CONCURRENTLY +
            // suppressTransaction, back when match_participants was ~35 GB and
            // migrations applied at API startup. Neither holds any more (#1227):
            // migrations apply out-of-band as `dotnet ef migrations script --idempotent`
            // piped into `psql --single-transaction`, where every statement is wrapped
            // in a `DO $EF$ ... END $EF$` PL/pgSQL block — and Postgres refuses
            // CONCURRENTLY both inside a function and inside a transaction block. The
            // flag only ever had an effect for Database.MigrateAsync(), a path that is
            // permanently disabled in preprod and prod.
            //
            // This body only re-executes on a database that does not yet carry the
            // migration in __EFMigrationsHistory — i.e. a brand-new one (fresh preprod,
            // DR restore, onboarding), where match_participants is empty and the build
            // is instantaneous. Existing databases skip it entirely. Same trade-off as
            // AddMatchParticipantFullPoolIndex.
            //
            // IF NOT EXISTS is kept so the statement also no-ops where the index was
            // pre-created out of band.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_match_participants_champion_position_tracked\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\") WHERE \"RiotAccountId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_match_participants_champion_position_tracked\";");
        }
    }
}
