using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionAggregateRunePages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_aggregate_rune_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstItemId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryKeystoneId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk1Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk2Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk3Id = table.Column<int>(type: "integer", nullable: false),
                    SecondaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    SecondaryPerk1Id = table.Column<int>(type: "integer", nullable: false),
                    SecondaryPerk2Id = table.Column<int>(type: "integer", nullable: false),
                    StatOffense = table.Column<int>(type: "integer", nullable: false),
                    StatFlex = table.Column<int>(type: "integer", nullable: false),
                    StatDefense = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_rune_pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_rune_pages_champion_aggregate_scopes_Sco~",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId",
                table: "champion_aggregate_rune_pages",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId_FirstItemId",
                table: "champion_aggregate_rune_pages",
                columns: new[] { "ScopeId", "FirstItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId_FirstItemId_PrimaryS~",
                table: "champion_aggregate_rune_pages",
                columns: new[] { "ScopeId", "FirstItemId", "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id", "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id", "StatOffense", "StatFlex", "StatDefense" },
                unique: true);

            // No backfill on purpose. The original version of this migration
            // tried to rebuild rune pages from match_participants ⋈
            // participant_perk_selections in raw SQL, but FirstItemId — the
            // correlation key with ChampionAggregateBuild.BuildItem0 — can
            // only be derived by replaying FinalBuildResolver over each
            // participant's item events. Backfilling FirstItemId = 0 produced
            // rows that were unusable for build-correlated queries (always
            // bucketed under "unknown") and biased global stats with a fake
            // catch-all bucket. ChampionPatternAggregationProcess replaces
            // each scope wholesale on its next run (cascade drops the
            // dimension rows), so the table fills with correctly-correlated
            // rune pages on the first post-deploy aggregation cycle.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_aggregate_rune_pages");
        }
    }
}
