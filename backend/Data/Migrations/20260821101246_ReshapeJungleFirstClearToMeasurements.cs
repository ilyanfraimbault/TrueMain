using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeJungleFirstClearToMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every stored row is unusable (#1188): the old builder credited one
            // camp per minute-frame, so its "camp sequence" was really the
            // jungler's position at minutes 2..7 and a full clear could never be
            // reported faster than 6:00 for a clear that really ends near 3:15.
            // The rows must go rather than be renamed into the new shape — the
            // old {Camp, TimestampMs} documents would deserialize into
            // JungleClearSample with silent default JungleCs/X/Y. The table
            // refills as new matches ingest.
            migrationBuilder.Sql("DELETE FROM jungle_first_clears;");

            migrationBuilder.RenameColumn(
                name: "Steps",
                table: "jungle_first_clears",
                newName: "Samples");

            migrationBuilder.AddColumn<string>(
                name: "StartCamp",
                table: "jungle_first_clears",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StartCamp",
                table: "jungle_first_clears");

            // Symmetric to Up: the measurement documents cannot be expressed as
            // the old camp sequence, so the rows go rather than be reinterpreted.
            migrationBuilder.Sql("DELETE FROM jungle_first_clears;");

            migrationBuilder.RenameColumn(
                name: "Samples",
                table: "jungle_first_clears",
                newName: "Steps");
        }
    }
}
