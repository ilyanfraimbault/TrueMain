using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantEloBracketPendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same trade-off as IX_match_participants_team_position_pending (#2095): the index
            // holds a few thousand rows but its build reads the whole match_participants heap
            // under a SHARE lock that holds the ingestor's writes for that pass. On preprod and
            // prod it is pre-created CONCURRENTLY before the deploy (#2303, "Pre-building a
            // heavy index out of band" in docs/production-migrations.md), and IF NOT EXISTS
            // turns this statement into a no-op there. A fresh database builds it instantly.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_match_participants_elo_bracket_pending\" " +
                "ON match_participants (\"Id\") WHERE \"RiotAccountId\" IS NOT NULL AND elo_bracket = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_match_participants_elo_bracket_pending\";");
        }
    }
}
