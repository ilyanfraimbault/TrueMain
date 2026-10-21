using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantTeamPositionPendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The index is tiny but its build is not: finding the unresolved rows reads
            // the whole match_participants heap, under a SHARE lock that holds the
            // ingestor's writes for the length of that pass. On preprod and prod it is
            // therefore pre-created CONCURRENTLY before the deploy (#2095, see
            // "Pre-building a heavy index out of band" in docs/production-migrations.md),
            // and IF NOT EXISTS turns this statement into a no-op there.
            //
            // Plain and transactional otherwise, so the migrations alone still rebuild the
            // schema: on a fresh database the table is empty and the build instantaneous,
            // and an environment that skipped the pre-build still gets the index, at the
            // cost of that one write stall — reads are never blocked.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_match_participants_team_position_pending\" " +
                "ON match_participants (\"MatchId\", \"TeamId\") WHERE \"TeamPosition\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_match_participants_team_position_pending\";");
        }
    }
}
