using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Moves the item context from a flat per-item grain to the edges of the build tree
    /// (#1496): every counter and every verdict is now scoped to the branch the decision was
    /// made on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The existing rows are <b>discarded, not migrated</b>. There is no parent to back-fill
    /// them with — the counters were folded without ever recording which step an item was —
    /// and a row whose <c>ParentItemId</c> defaulted to 0 would claim every item was the
    /// first one. The three tables are therefore truncated and the per-match flag is cleared
    /// so the fold rebuilds them from the retained matches, which is what it is designed to
    /// do: the counters are additive per match and the verdicts derived from them.
    /// </para>
    /// <para>
    /// Cheap enough for the out-of-band migration job: three truncations of tables in the
    /// hundreds of thousands of rows, plus one flag update over the match table. The refold
    /// itself then happens over the following ingestor runs, capped by
    /// <c>MaxMatchesPerRun</c>, so it never lands as one long transaction.
    /// </para>
    /// </remarks>
    public partial class ScopeItemContextToBuildBranches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_item_context_verdicts_grain",
                table: "champion_item_context_verdicts");

            migrationBuilder.DropIndex(
                name: "IX_champion_item_context_totals_grain",
                table: "champion_item_context_totals");

            migrationBuilder.DropIndex(
                name: "IX_champion_item_context_stats_grain",
                table: "champion_item_context_stats");

            migrationBuilder.Sql("""
                TRUNCATE TABLE champion_item_context_stats,
                               champion_item_context_totals,
                               champion_item_context_verdicts;
                """);

            migrationBuilder.DropColumn(
                name: "SlotGames",
                table: "champion_item_context_verdicts");

            migrationBuilder.AddColumn<int>(
                name: "ParentItemId",
                table: "champion_item_context_verdicts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchGames",
                table: "champion_item_context_verdicts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentItemId",
                table: "champion_item_context_totals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentItemId",
                table: "champion_item_context_stats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_verdicts_grain",
                table: "champion_item_context_verdicts",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ParentItemId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_totals_grain",
                table: "champion_item_context_totals",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ParentItemId", "Axis", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_stats_grain",
                table: "champion_item_context_stats",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ParentItemId", "ItemId", "Axis", "Bucket" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE matches SET "ItemContextAggregated" = false WHERE "ItemContextAggregated";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_item_context_verdicts_grain",
                table: "champion_item_context_verdicts");

            migrationBuilder.DropIndex(
                name: "IX_champion_item_context_totals_grain",
                table: "champion_item_context_totals");

            migrationBuilder.DropIndex(
                name: "IX_champion_item_context_stats_grain",
                table: "champion_item_context_stats");

            migrationBuilder.Sql("""
                TRUNCATE TABLE champion_item_context_stats,
                               champion_item_context_totals,
                               champion_item_context_verdicts;
                """);

            migrationBuilder.DropColumn(
                name: "BranchGames",
                table: "champion_item_context_verdicts");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                table: "champion_item_context_verdicts");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                table: "champion_item_context_totals");

            migrationBuilder.DropColumn(
                name: "ParentItemId",
                table: "champion_item_context_stats");

            migrationBuilder.AddColumn<int>(
                name: "SlotGames",
                table: "champion_item_context_verdicts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_verdicts_grain",
                table: "champion_item_context_verdicts",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_totals_grain",
                table: "champion_item_context_totals",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "Axis", "Bucket" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_item_context_stats_grain",
                table: "champion_item_context_stats",
                columns: new[] { "Patch", "ChampionId", "Position", "Slot", "ItemId", "Axis", "Bucket" },
                unique: true);

            migrationBuilder.Sql("""
                UPDATE matches SET "ItemContextAggregated" = false WHERE "ItemContextAggregated";
                """);
        }
    }
}
