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
            // Order is the whole point of this migration. The deploy path applies every
            // pending migration inside ONE transaction (`psql --single-transaction`, see
            // docs/production-migrations.md), and Postgres never releases or downgrades a
            // lock before that transaction commits. So what matters is not which locks
            // are taken but how long each is held:
            //
            //   * CREATE INDEX takes SHARE on the table: writes block, reads do not.
            //   * DROP INDEX takes ACCESS EXCLUSIVE: *everything* blocks, reads included.
            //
            // Dropping first — the shape EF generates for an INCLUDE change — would hold
            // ACCESS EXCLUSIVE through both builds, stalling every champion page and
            // recommendation still being served by the outgoing release while the
            // migrate job runs. Instead the covering replacements are built first under
            // temporary names (reads keep flowing; only the ingestor's writes wait, and
            // its retries absorb that), and the old indexes are dropped and the new ones
            // renamed into their place as the very last statements, so the exclusive
            // lock is held for milliseconds before commit.
            //
            // What that cannot remove: acquiring ACCESS EXCLUSIVE still waits for reads
            // already in flight to finish, and queues new ones behind it meanwhile — the
            // same brief queue every DDL migration on this table already accepts. It is
            // bounded by the slowest in-flight read, not by the index builds.
            //
            // CONCURRENTLY is not an option: the idempotent script wraps each statement in
            // a DO block, where Postgres rejects it (#1227), and CI's migrate-fresh job
            // enforces that. Build time was 3.6 s for the larger index on a 776 MB bench
            // reconstruction; production's table is ~11 GB, so expect it proportionally
            // longer — a window in which ingestion writes wait, not one in which reads do.
            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_full_covering",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition", "elo_bracket" })
                .Annotation("Npgsql:IndexInclude", new[] { "MatchId", "ParticipantId", "TeamId", "Win", "Puuid" });

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId_ParticipantId_covering",
                table: "match_participants",
                columns: new[] { "MatchId", "ParticipantId" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "TeamId", "TeamPosition", "ChampionId", "Win", "Puuid" });

            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants");

            migrationBuilder.RenameIndex(
                name: "IX_match_participants_champion_position_full_covering",
                table: "match_participants",
                newName: "IX_match_participants_champion_position_full");

            migrationBuilder.RenameIndex(
                name: "IX_match_participants_MatchId_ParticipantId_covering",
                table: "match_participants",
                newName: "IX_match_participants_MatchId_ParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Same ordering as Up, for the same reason: build first, swap last.
            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_full_plain",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition", "elo_bracket" });

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId_ParticipantId_plain",
                table: "match_participants",
                columns: new[] { "MatchId", "ParticipantId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_full",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants");

            migrationBuilder.RenameIndex(
                name: "IX_match_participants_champion_position_full_plain",
                table: "match_participants",
                newName: "IX_match_participants_champion_position_full");

            migrationBuilder.RenameIndex(
                name: "IX_match_participants_MatchId_ParticipantId_plain",
                table: "match_participants",
                newName: "IX_match_participants_MatchId_ParticipantId");
        }
    }
}
