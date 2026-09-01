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

            // The index name is reused, so the old one must be dropped before the new
            // one is built. Both statements are plain and transactional: they used
            // CONCURRENTLY + suppressTransaction back when match_participants was ~35 GB
            // and migrations applied at API startup, but that flag only ever had an
            // effect for Database.MigrateAsync(). The deploy path is an `--idempotent`
            // script piped into `psql --single-transaction`, where each statement sits
            // in a `DO $EF$ ... END $EF$` block and Postgres rejects CONCURRENTLY both
            // inside a function and inside a transaction block (#1227) — so this
            // migration could never have applied to a database created from scratch.
            //
            // Keeping it transactional is also what makes this migration coherent: the
            // AddColumn above it is transactional, and the index depends on the column.
            // The body only re-executes where __EFMigrationsHistory lacks this
            // migration, i.e. a brand-new database with an empty table.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_match_participants_champion_position_tracked\";");

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_match_participants_champion_position_tracked\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\", \"elo_bracket\") " +
                "WHERE \"RiotAccountId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the elo-bracket index first, so the column it references can then be
            // dropped. Transactional like the rest of the migration (#1227).
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_match_participants_champion_position_tracked\";");

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

            // Rebuild the original (pre-elo-bracket) index, matching the sibling
            // AddMatchParticipantChampionPositionIndex migration.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_match_participants_champion_position_tracked\" " +
                "ON match_participants (\"ChampionId\", \"TeamPosition\") WHERE \"RiotAccountId\" IS NOT NULL;");
        }
    }
}
