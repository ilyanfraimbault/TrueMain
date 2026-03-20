using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class RefactorChampionPatternAggregatesForBuildPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_pattern_aggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    GameVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    SubStyleId = table.Column<int>(type: "integer", nullable: false),
                    PerksOffense = table.Column<int>(type: "integer", nullable: false),
                    PerksFlex = table.Column<int>(type: "integer", nullable: false),
                    PerksDefense = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell1Id = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell2Id = table.Column<int>(type: "integer", nullable: false),
                    SkillOrderKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StarterItems = table.Column<List<int>>(type: "jsonb", nullable: false),
                    StarterItemsKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BuildItem0 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem1 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem2 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem3 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem4 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem5 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem6 = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    LastGameStartTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_pattern_aggregates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_pattern_aggregates_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameV~1",
                table: "champion_pattern_aggregates",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "PrimaryStyleId", "SubStyleId", "PerksOffense", "PerksFlex", "PerksDefense", "SummonerSpell1Id", "SummonerSpell2Id", "SkillOrderKey", "StarterItemsKey", "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameVe~",
                table: "champion_pattern_aggregates",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "Position" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_pattern_aggregates");
        }
    }
}
