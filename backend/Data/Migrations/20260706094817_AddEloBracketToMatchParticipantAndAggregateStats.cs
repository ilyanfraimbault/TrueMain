using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEloBracketToMatchParticipantAndAggregateStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_tracked",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats");

            migrationBuilder.DropIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats");

            migrationBuilder.AddColumn<string>(
                name: "elo_bracket",
                table: "match_participants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "elo_bracket",
                table: "champion_timeline_lead_stats",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "elo_bracket",
                table: "champion_matchup_stats",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_tracked",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition", "elo_bracket" },
                filter: "\"RiotAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "IntervalMinute", "elo_bracket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats",
                columns: new[] { "ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_match_participants_champion_position_tracked",
                table: "match_participants");

            migrationBuilder.DropIndex(
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats");

            migrationBuilder.DropIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats");

            migrationBuilder.DropColumn(
                name: "elo_bracket",
                table: "match_participants");

            migrationBuilder.DropColumn(
                name: "elo_bracket",
                table: "champion_timeline_lead_stats");

            migrationBuilder.DropColumn(
                name: "elo_bracket",
                table: "champion_matchup_stats");

            migrationBuilder.CreateIndex(
                name: "IX_match_participants_champion_position_tracked",
                table: "match_participants",
                columns: new[] { "ChampionId", "TeamPosition" },
                filter: "\"RiotAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "IntervalMinute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats",
                columns: new[] { "ChampionId", "TeamPosition", "OpponentChampionId", "Patch" },
                unique: true);
        }
    }
}
