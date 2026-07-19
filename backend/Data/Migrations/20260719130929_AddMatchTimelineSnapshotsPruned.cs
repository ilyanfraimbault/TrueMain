using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchTimelineSnapshotsPruned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TimelineSnapshotsPruned",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_matches_snapshot_prune_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"PowerspikeAggregated\" = true AND \"TimelineSnapshotsPruned\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_snapshot_prune_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "TimelineSnapshotsPruned",
                table: "matches");
        }
    }
}
