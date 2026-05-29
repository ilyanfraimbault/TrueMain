using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMatchParticipantIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Participants are inserted in bulk during ingestion and nothing
            // previously prevented duplicate (MatchId, ParticipantId) rows.
            // Deduplicate any existing offenders — keeping a single arbitrary
            // physical row per group via ctid — before adding the unique index
            // that prevents the drift in the future.
            migrationBuilder.Sql(
                """
                DELETE FROM match_participants AS mp
                WHERE mp.ctid <> (
                    SELECT MIN(dup.ctid)
                    FROM match_participants AS dup
                    WHERE dup."MatchId" = mp."MatchId"
                      AND dup."ParticipantId" = mp."ParticipantId"
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants",
                columns: new[] { "MatchId", "ParticipantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId_ParticipantId",
                table: "match_participants");
        }
    }
}
