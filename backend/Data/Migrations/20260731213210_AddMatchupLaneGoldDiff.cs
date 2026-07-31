using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchupLaneGoldDiff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LaneGoldDiffGames",
                table: "champion_matchup_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LaneGoldDiffSum",
                table: "champion_matchup_stats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaneGoldDiffGames",
                table: "champion_matchup_stats");

            migrationBuilder.DropColumn(
                name: "LaneGoldDiffSum",
                table: "champion_matchup_stats");
        }
    }
}
