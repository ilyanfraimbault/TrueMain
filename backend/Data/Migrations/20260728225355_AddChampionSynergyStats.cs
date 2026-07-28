using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionSynergyStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Incremental per-match fold flag, mirroring MatchupLeadAggregated (#811)
            // and PowerspikeAggregated (#694). Deliberately left at false for every
            // existing row — the opposite of what AddChampionMatchupLeadAggregatedFlag
            // had to do. That migration backfilled to true because a full-recompute pass
            // had ALREADY written every match's games/wins into champion_matchup_stats,
            // so folding them again would have double-counted. The synergy tables below
            // are created empty by this same migration, so there is nothing to
            // double-count and every retained match still has to be folded exactly once.
            // ChampionSynergyAggregationProcess drains that backlog across runs
            // (SynergyAggregation:MaxMatchesPerRun).
            migrationBuilder.AddColumn<bool>(
                name: "SynergyAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_synergy_baseline_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Side = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_synergy_baseline_stats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_synergy_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PartnerChampionId = table.Column<int>(type: "integer", nullable: false),
                    PartnerPosition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    elo_bracket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: ""),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_synergy_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_synergy_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"SynergyAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_champion_synergy_baseline_stats_grain",
                table: "champion_synergy_baseline_stats",
                columns: new[] { "Side", "ChampionId", "TeamPosition", "Patch", "elo_bracket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_synergy_stats_grain",
                table: "champion_synergy_stats",
                columns: new[] { "ChampionId", "TeamPosition", "PartnerChampionId", "PartnerPosition", "Patch", "elo_bracket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_synergy_baseline_stats");

            migrationBuilder.DropTable(
                name: "champion_synergy_stats");

            migrationBuilder.DropIndex(
                name: "IX_matches_synergy_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "SynergyAggregated",
                table: "matches");
        }
    }
}
