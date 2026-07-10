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
            // Aggregate rollup tables are small — rebuilding their unique indexes
            // inside the migration transaction is cheap and safe.
            migrationBuilder.DropIndex(
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats");

            migrationBuilder.DropIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats");

            // Adding a NOT NULL column with a constant default is a metadata-only
            // change in PostgreSQL 11+, so this is fast even on the 35 GB
            // match_participants table.
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
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "IntervalMinute", "elo_bracket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats",
                columns: new[] { "ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket" },
                unique: true);

            // match_participants is ~35 GB. Swapping its index in-transaction would take
            // an ACCESS EXCLUSIVE lock and rebuild the whole index, blowing the startup
            // migration command timeout and stalling the live ingestor. Do it
            // CONCURRENTLY, outside the transaction — same approach as the sibling
            // AddMatchParticipantChampionPositionIndex migration. The index name is
            // reused, so the old one must be dropped before the new one is built.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_match_participants_champion_position_tracked\";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_match_participants_champion_position_tracked\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\", \"elo_bracket\") " +
                "WHERE \"RiotAccountId\" IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the elo-bracket index on the 35 GB table CONCURRENTLY first, so the
            // column it references can then be dropped inside the transaction.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_match_participants_champion_position_tracked\";",
                suppressTransaction: true);

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
                name: "IX_champion_timeline_lead_stats_ChampionId_TeamPosition_Patch_~",
                table: "champion_timeline_lead_stats",
                columns: new[] { "ChampionId", "TeamPosition", "Patch", "IntervalMinute" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_matchup_stats_ChampionId_TeamPosition_OpponentCham~",
                table: "champion_matchup_stats",
                columns: new[] { "ChampionId", "TeamPosition", "OpponentChampionId", "Patch" },
                unique: true);

            // Rebuild the original (pre-elo-bracket) index CONCURRENTLY, matching the
            // sibling AddMatchParticipantChampionPositionIndex migration.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_match_participants_champion_position_tracked\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\") WHERE \"RiotAccountId\" IS NOT NULL;",
                suppressTransaction: true);
        }
    }
}
