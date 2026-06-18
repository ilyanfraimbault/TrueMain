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
            // CONCURRENTLY so building the index on the 35 GB match_participants table
            // does not take a write lock and stall the live ingestor. It cannot run in
            // a transaction, hence suppressTransaction.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_match_participants_champion_position_tracked\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\") WHERE \"RiotAccountId\" IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_match_participants_champion_position_tracked\";",
                suppressTransaction: true);
        }
    }
}
