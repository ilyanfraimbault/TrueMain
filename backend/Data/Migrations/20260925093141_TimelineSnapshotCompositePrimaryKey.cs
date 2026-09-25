using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Makes the natural key <c>(MatchId, ParticipantId, IntervalMinute)</c> the primary key of
    /// match_participant_timeline_snapshots and drops the surrogate <c>Id</c> (#1697). Nothing ever
    /// read a snapshot by <c>Id</c>; its B-tree over random UUIDs was pure write and disk cost.
    /// </summary>
    /// <remarks>
    /// Hand-written instead of the scaffolded DropIndex + AddPrimaryKey, which would rebuild the
    /// composite index from scratch under an exclusive lock on one of the largest tables.
    /// <c>ADD CONSTRAINT ... PRIMARY KEY USING INDEX</c> promotes the existing unique index (its
    /// columns are already NOT NULL) and renames it to the constraint's name, so the whole Up is
    /// catalog-only: dropping the old PK drops its index, and <c>DROP COLUMN</c> does not rewrite
    /// the heap — the 16 bytes per row are reclaimed as retention cycles rows out. Transactional,
    /// no <c>CONCURRENTLY</c> (#1227).
    /// </remarks>
    public partial class TimelineSnapshotCompositePrimaryKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_match_participant_timeline_snapshots",
                table: "match_participant_timeline_snapshots");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "match_participant_timeline_snapshots");

            migrationBuilder.Sql(
                "ALTER TABLE match_participant_timeline_snapshots " +
                "ADD CONSTRAINT \"PK_match_participant_timeline_snapshots\" " +
                "PRIMARY KEY USING INDEX \"IX_match_participant_timeline_snapshots_MatchId_ParticipantId_~\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_match_participant_timeline_snapshots_MatchId_ParticipantId_~",
                table: "match_participant_timeline_snapshots",
                columns: new[] { "MatchId", "ParticipantId", "IntervalMinute" },
                unique: true);

            migrationBuilder.DropPrimaryKey(
                name: "PK_match_participant_timeline_snapshots",
                table: "match_participant_timeline_snapshots");

            // A fresh UUID per existing row: a constant default would collide on the PK.
            migrationBuilder.Sql(
                "ALTER TABLE match_participant_timeline_snapshots " +
                "ADD COLUMN \"Id\" uuid NOT NULL DEFAULT gen_random_uuid();");

            migrationBuilder.Sql(
                "ALTER TABLE match_participant_timeline_snapshots ALTER COLUMN \"Id\" DROP DEFAULT;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_match_participant_timeline_snapshots",
                table: "match_participant_timeline_snapshots",
                column: "Id");
        }
    }
}
