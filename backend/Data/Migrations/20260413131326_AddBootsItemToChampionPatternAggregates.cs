using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBootsItemToChampionPatternAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameV~1",
                table: "champion_pattern_aggregates");

            migrationBuilder.AddColumn<int>(
                name: "BootsItemId",
                table: "champion_pattern_aggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameV~1",
                table: "champion_pattern_aggregates",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "PrimaryStyleId", "SubStyleId", "PerksOffense", "PerksFlex", "PerksDefense", "SummonerSpell1Id", "SummonerSpell2Id", "SkillOrderKey", "StarterItemsKey", "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameV~1",
                table: "champion_pattern_aggregates");

            migrationBuilder.DropColumn(
                name: "BootsItemId",
                table: "champion_pattern_aggregates");

            migrationBuilder.CreateIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameV~1",
                table: "champion_pattern_aggregates",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "PrimaryStyleId", "SubStyleId", "PerksOffense", "PerksFlex", "PerksDefense", "SummonerSpell1Id", "SummonerSpell2Id", "SkillOrderKey", "StarterItemsKey", "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6" },
                unique: true);
        }
    }
}
