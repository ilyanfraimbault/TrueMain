using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class PurgeOrphanPerkSelectionsAndFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mid-flow rollbacks in the previous MatchSnapshotWriter could
            // commit perk_selection rows whose MatchId never landed in the
            // matches table. Those orphans now block re-ingestion via the
            // (MatchId, ParticipantId, PerkSelectionCatalogId) unique
            // index — purge them before adding the hard FK that prevents
            // the same drift in the future.
            migrationBuilder.Sql(
                """
                DELETE FROM participant_perk_selections AS pps
                WHERE NOT EXISTS (
                    SELECT 1 FROM matches AS m WHERE m."Id" = pps."MatchId"
                );
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_participant_perk_selections_matches_MatchId",
                table: "participant_perk_selections",
                column: "MatchId",
                principalTable: "matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_participant_perk_selections_matches_MatchId",
                table: "participant_perk_selections");
        }
    }
}
