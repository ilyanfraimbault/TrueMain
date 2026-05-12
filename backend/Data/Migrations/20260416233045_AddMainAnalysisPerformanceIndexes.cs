using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainAnalysisPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_matches_platform_queue_game_start"
                ON "matches" ("PlatformId", "QueueId", "GameStartTimeUtc");
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_match_participants_puuid_match"
                ON "match_participants" ("Puuid", "MatchId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_matches_platform_queue_game_start";
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_match_participants_puuid_match";
                """);
        }
    }
}
