using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchupLaneOutcomeCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LaneOutcomeAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LaneGames",
                table: "champion_matchup_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LaneLosses",
                table: "champion_matchup_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LaneWins",
                table: "champion_matchup_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_matches_lane_outcome_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"LaneOutcomeAggregated\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_matches_lane_outcome_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "LaneOutcomeAggregated",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "LaneGames",
                table: "champion_matchup_stats");

            migrationBuilder.DropColumn(
                name: "LaneLosses",
                table: "champion_matchup_stats");

            migrationBuilder.DropColumn(
                name: "LaneWins",
                table: "champion_matchup_stats");
        }
    }
}
