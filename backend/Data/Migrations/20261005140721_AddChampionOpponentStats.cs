using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionOpponentStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OpponentAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_opponent_baseline_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EloBracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_opponent_baseline_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_opponent_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    OpponentPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EloBracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_opponent_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_opponent_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"OpponentAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_champion_opponent_baseline_stats_grain",
                table: "champion_opponent_baseline_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Side", "Patch", "EloBracket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_opponent_stats_grain",
                table: "champion_opponent_stats",
                columns: new[] { "ChampionId", "TeamPosition", "OpponentChampionId", "OpponentPosition", "Patch", "EloBracket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_opponent_baseline_stats");

            migrationBuilder.DropTable(
                name: "champion_opponent_stats");

            migrationBuilder.DropIndex(
                name: "IX_matches_opponent_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "OpponentAggregated",
                table: "matches");
        }
    }
}
