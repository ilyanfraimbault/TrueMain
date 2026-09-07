using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Puts the ordering column into the item-context fold's pending index (#1514), so
    /// dequeuing the newest matches first is an index scan with a LIMIT rather than a sort of
    /// the whole backlog on every batch.
    /// </summary>
    /// <remarks>
    /// Cheap: the index is partial on <c>"ItemContextAggregated" = false</c>, so it covers the
    /// pending tail rather than the match table, and it shrinks to nothing once a backlog has
    /// drained.
    /// </remarks>
    public partial class ItemContextPendingIndexByGameStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_item_context_pending",
                table: "matches");

            migrationBuilder.CreateIndex(
                name: "IX_matches_item_context_pending",
                table: "matches",
                columns: new[] { "QueueId", "GameStartTimeUtc" },
                descending: new[] { false, true },
                filter: "\"ItemContextAggregated\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_item_context_pending",
                table: "matches");

            migrationBuilder.CreateIndex(
                name: "IX_matches_item_context_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"ItemContextAggregated\" = false");
        }
    }
}
