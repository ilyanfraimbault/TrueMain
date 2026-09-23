using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantCoveringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain, transactional DROP + CREATE, following the same reasoning as
            // AddMatchParticipantFullPoolIndex (#563): at this table's size a btree build
            // is seconds, not minutes, so it stays well inside the migrator's command
            // timeout and cannot crash-loop a rollout the way a slow CONCURRENTLY build
            // did in #598. Measured on a 2M-row reconstruction: 3.6 s for the unique
            // index, the larger of the two.
            //
            // Both indexes are recreated inside one transaction, so the unique constraint
            // is never absent to any other session. Migrations apply out-of-band ahead of
            // the deploy (docs/production-migrations.md), and only the ingestor writes to
            // this table, so the SHARE lock stalls ingestion for those seconds and its
            // retries absorb it.
            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants");

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition", "elo_bracket" })
                .Annotation("Npgsql:IndexInclude", new[] { "MatchId", "ParticipantId", "TeamId", "Win", "Puuid" });

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants",
                columns: new[] { "MatchId", "ParticipantId" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "TeamId", "TeamPosition", "ChampionId", "Win", "Puuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants");

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition", "elo_bracket" });

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants",
                columns: new[] { "MatchId", "ParticipantId" },
                unique: true);
        }
    }
}
