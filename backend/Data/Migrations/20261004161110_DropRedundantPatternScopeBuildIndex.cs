using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Drops <c>IX_champion_aggregate_patterns_ScopeId_BuildId</c> (#125): it is a strict prefix of the
    /// UNIQUE <c>(ScopeId, BuildId, RunePageId, SkillOrderId, SpellPairId, StarterItemsId)</c> index,
    /// which serves every lookup the two-column one could. Plain transactional <c>DROP INDEX</c>,
    /// catalog-only, no <c>CONCURRENTLY</c> (#1227).
    /// </summary>
    public partial class DropRedundantPatternScopeBuildIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_BuildId",
                table: "champion_aggregate_patterns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_BuildId",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "BuildId" });
        }
    }
}
