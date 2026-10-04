using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Makes the natural key <c>(MatchId, ParticipantId, PerkSelectionCatalogId)</c> the primary key of
    /// participant_perk_selections and drops the surrogate <c>Id</c> (#124). No table references the row
    /// and nothing ever read a selection by <c>Id</c>; its B-tree over random UUIDs was pure write and
    /// disk cost.
    /// </summary>
    /// <remarks>
    /// Hand-written instead of the scaffolded DropIndex + AddPrimaryKey, which would rebuild the
    /// composite index from scratch under an exclusive lock. Same shape as #1697:
    /// <c>ADD CONSTRAINT ... PRIMARY KEY USING INDEX</c> promotes the existing unique index (its
    /// columns are already NOT NULL) and renames it to the constraint's name, so the whole Up is
    /// catalog-only: dropping the old PK drops its index, and <c>DROP COLUMN</c> does not rewrite
    /// the heap — the 16 bytes per row are reclaimed as retention cycles rows out. Transactional,
    /// no <c>CONCURRENTLY</c> (#1227).
    /// </remarks>
    public partial class ParticipantPerkSelectionCompositePrimaryKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_participant_perk_selections",
                table: "participant_perk_selections");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "participant_perk_selections");

            migrationBuilder.Sql(
                "ALTER TABLE participant_perk_selections " +
                "ADD CONSTRAINT \"PK_participant_perk_selections\" " +
                "PRIMARY KEY USING INDEX \"IX_participant_perk_selections_MatchId_ParticipantId_PerkSelec~\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_participant_perk_selections_MatchId_ParticipantId_PerkSelec~",
                table: "participant_perk_selections",
                columns: new[] { "MatchId", "ParticipantId", "PerkSelectionCatalogId" },
                unique: true);

            migrationBuilder.DropPrimaryKey(
                name: "PK_participant_perk_selections",
                table: "participant_perk_selections");

            // A fresh UUID per existing row: a constant default would collide on the PK.
            migrationBuilder.Sql(
                "ALTER TABLE participant_perk_selections " +
                "ADD COLUMN \"Id\" uuid NOT NULL DEFAULT gen_random_uuid();");

            migrationBuilder.Sql(
                "ALTER TABLE participant_perk_selections ALTER COLUMN \"Id\" DROP DEFAULT;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_participant_perk_selections",
                table: "participant_perk_selections",
                column: "Id");
        }
    }
}
