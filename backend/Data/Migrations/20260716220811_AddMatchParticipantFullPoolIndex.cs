using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantFullPoolIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CONCURRENTLY so building the index on the multi-GB match_participants
            // table does not take a write lock and stall the live ingestor — it
            // cannot run in a transaction, hence suppressTransaction. IF NOT EXISTS
            // keeps the startup migration a fast no-op in prod, where the index is
            // pre-created out-of-band before the rollout (a full-pool build can
            // exceed the startup command timeout, see #598).
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_match_participants_champion_position_full\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\", \"elo_bracket\");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_match_participants_champion_position_full\";",
                suppressTransaction: true);
        }
    }
}
