using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceChampionStatInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT VALID, hand-written: EF's AddCheckConstraint would scan every stat table
            // under an ACCESS EXCLUSIVE lock and fail the deploy on any frozen row folded
            // under older rules. Every insert and update is checked from here on; the rows
            // already written are not (decisions/data-aggregation.md, #1365).
            foreach (var (table, name, sql) in Constraints)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ADD CONSTRAINT \"{name}\" CHECK ({sql}) NOT VALID;");
            }
        }

        private static readonly (string Table, string Name, string Sql)[] Constraints =
        [
            ("champion_synergy_stats", "CK_champion_synergy_stats_CanonicalPartnerPosition", "\"PartnerPosition\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_synergy_stats", "CK_champion_synergy_stats_CanonicalTeamPosition", "\"TeamPosition\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_synergy_stats", "CK_champion_synergy_stats_PartnerChampionId", "\"PartnerChampionId\" > 0"),
            ("champion_synergy_stats", "CK_champion_synergy_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_synergy_baseline_stats", "CK_champion_synergy_baseline_stats_CanonicalTeamPosition", "\"TeamPosition\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_synergy_baseline_stats", "CK_champion_synergy_baseline_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_profile_stats", "CK_champion_profile_stats_CanonicalPosition", "\"Position\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_profile_stats", "CK_champion_profile_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_opponent_stats", "CK_champion_opponent_stats_CanonicalTeamPosition", "\"TeamPosition\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_opponent_stats", "CK_champion_opponent_stats_OpponentChampionId", "\"OpponentChampionId\" > 0"),
            ("champion_opponent_stats", "CK_champion_opponent_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_opponent_baseline_stats", "CK_champion_opponent_baseline_stats_CanonicalTeamPosition", "\"TeamPosition\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_opponent_baseline_stats", "CK_champion_opponent_baseline_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_next_item_terms", "CK_champion_next_item_terms_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_matchup_stats", "CK_champion_matchup_stats_CanonicalTeamPosition", "\"TeamPosition\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY')"),
            ("champion_matchup_stats", "CK_champion_matchup_stats_LaneGamesWithinGames", "\"LaneGames\" <= \"Games\""),
            ("champion_matchup_stats", "CK_champion_matchup_stats_OpponentChampionId", "\"OpponentChampionId\" > 0"),
            ("champion_matchup_stats", "CK_champion_matchup_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_item_context_verdicts", "CK_champion_item_context_verdicts_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_item_context_totals", "CK_champion_item_context_totals_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_item_context_stats", "CK_champion_item_context_stats_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_aggregate_scopes", "CK_champion_aggregate_scopes_PositionOrNoLane", "\"Position\" IN ('TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY', '')"),
            ("champion_aggregate_scopes", "CK_champion_aggregate_scopes_WinsWithinGames", "\"Wins\" <= \"Games\""),
            ("champion_aggregate_patterns", "CK_champion_aggregate_patterns_WinsWithinGames", "\"Wins\" <= \"Games\""),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_synergy_stats_CanonicalPartnerPosition",
                table: "champion_synergy_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_synergy_stats_CanonicalTeamPosition",
                table: "champion_synergy_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_synergy_stats_PartnerChampionId",
                table: "champion_synergy_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_synergy_stats_WinsWithinGames",
                table: "champion_synergy_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_synergy_baseline_stats_CanonicalTeamPosition",
                table: "champion_synergy_baseline_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_synergy_baseline_stats_WinsWithinGames",
                table: "champion_synergy_baseline_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_profile_stats_CanonicalPosition",
                table: "champion_profile_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_profile_stats_WinsWithinGames",
                table: "champion_profile_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_opponent_stats_CanonicalTeamPosition",
                table: "champion_opponent_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_opponent_stats_OpponentChampionId",
                table: "champion_opponent_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_opponent_stats_WinsWithinGames",
                table: "champion_opponent_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_opponent_baseline_stats_CanonicalTeamPosition",
                table: "champion_opponent_baseline_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_opponent_baseline_stats_WinsWithinGames",
                table: "champion_opponent_baseline_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_next_item_terms_WinsWithinGames",
                table: "champion_next_item_terms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_matchup_stats_CanonicalTeamPosition",
                table: "champion_matchup_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_matchup_stats_LaneGamesWithinGames",
                table: "champion_matchup_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_matchup_stats_OpponentChampionId",
                table: "champion_matchup_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_matchup_stats_WinsWithinGames",
                table: "champion_matchup_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_item_context_verdicts_WinsWithinGames",
                table: "champion_item_context_verdicts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_item_context_totals_WinsWithinGames",
                table: "champion_item_context_totals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_item_context_stats_WinsWithinGames",
                table: "champion_item_context_stats");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_aggregate_scopes_PositionOrNoLane",
                table: "champion_aggregate_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_aggregate_scopes_WinsWithinGames",
                table: "champion_aggregate_scopes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_champion_aggregate_patterns_WinsWithinGames",
                table: "champion_aggregate_patterns");
        }
    }
}
