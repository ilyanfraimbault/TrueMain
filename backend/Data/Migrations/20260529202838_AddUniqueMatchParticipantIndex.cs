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
            // Collapse pre-existing duplicate (MatchId, ParticipantId) rows before the
            // unique index can be built — keep one arbitrary physical row per group via
            // ctid (PostgreSQL's physical row identifier). Without this dedup,
            // CreateIndex(unique: true) would throw on any historical duplicate.
            migrationBuilder.Sql(@"
                DELETE FROM match_participants a
                USING match_participants b
                WHERE a.ctid < b.ctid
                  AND a.""MatchId"" = b.""MatchId""
                  AND a.""ParticipantId"" = b.""ParticipantId"";");

            // The composite (MatchId, ParticipantId) index has MatchId as its leading
            // column, so it supersedes the standalone MatchId index.
            migrationBuilder.DropIndex(
                name: "IX_match_participants_MatchId",
                table: "match_participants");

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

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_MatchId",
                table: "match_participants",
                column: "MatchId");
        }
    }
}
